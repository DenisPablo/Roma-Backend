# Design: Productividad Tracking (Módulo 1)

## Technical Approach

App ASP.NET Core monoproceso y greenfield (C#, .NET 10 LTS): `Program.cs` cablea `TaskService`/`TaskRepository` (dominio CRUD + máquina de estados), `CategoryService`/`CategoryRepository` (lista gestionada), `DatabaseService` (Microsoft.Data.Sqlite + Dapper, WAL, migraciones SQL al boot) y `Notifications` — registrado **solo** si `NOTIFY_ENABLED=true` — con `NotificationWorker` (BackgroundService + PeriodicTimer 60s, minute ticker + chequeo EOD) sobre el puerto `INotifierPort`, implementado por `TelegramNotifier` (Telegram.Bot send-only, sin polling/webhook). Cumple las tres specs: CHECKs de estados en DB, dedup `reminder_sent_at`, avisos efímeros, resumen EOD idempotente.

## Architecture Decisions

| # | Decision | Options considered | Tradeoff | Chosen |
|---|----------|--------------------|----------|--------|
| 1 | Persistencia | Dapper + Microsoft.Data.Sqlite vs EF Core vs raw ADO.NET | SQL directo y migraciones triviales vs ORM con tracking innecesario para 1-2 tablas | **Dapper + Microsoft.Data.Sqlite** (DatabaseService) |
| 2 | Worker | Mismo proceso vs proceso separado | 1 comando/1 DB vs aislamiento de crash | **Mismo proceso**; `NOTIFY_ENABLED=false` omite `AddHostedService<NotificationWorker>` (no instancia ni bot ni scheduler) |
| 3 | `not_done` en migración | CHECK completo en 001 vs ALTER posterior | DB greenfield, nunca existió el esquema viejo | **001 incluye `not_done`** en el CHECK; sin ALTER. Bi-condicionales por estado |
| 4 | Seed de categorías | En migración SQL vs runtime al boot | Seed depende de env (`CATEGORIES`); las migraciones son versionadas | **Runtime en `DatabaseService.Initialize()`** (al arranque): `INSERT` solo si `COUNT(categories)=0`; split por coma + trim; defaults si env ausente |
| 5 | Idempotencia EOD | Tabla `day_summaries(day PK)` vs dedup por marca en tasks | PK explícita = invariante "una por día" verificable en DB | **`day_summaries`**: check → send → insert |
| 6 | Rollover day-close | Solo en cron EOD vs sweep en ticker | Cron 23:55 se pierde si la app está apagada | **Sweep idempotente en cada tick + `Initialize`** (`pending` y `day < hoy` → `not_done`) |
| 7 | Duración | Columna `duration_seconds` vs cálculo en lectura | Spec: "computed only when completing" | **Columna seteada en `complete`**; formato h/m en capa API |
| 8 | Re-notificación | Lógica en service vs CASE en SQL | Atómico vs explicito | **`CASE` dentro del UPDATE** de día/hora (nuevo momento futuro + `reminder_sent_at` set → NULL) |
| 9 | Falla de red | Cola persistente vs log+retry | Sin estado extra vs duplicados raros | **log + 1 reintento**; si sigue fallando `reminder_sent_at` queda NULL y el worker reintenta mientras la tarea siga due (sin cola) |
| 10 | Scheduler | Quartz.NET vs BackgroundService + PeriodicTimer | Cron rico vs sin dependencia extra para un tick por minuto + chequeo EOD | **BackgroundService + PeriodicTimer(60s)** + comparación con `EOD_TIME` |

## Data Flow

```
Web (127.0.0.1) ─→ Controllers ─→ Services ─→ Repositories ─→ SQLite (WAL)
                                                              ▲
NotificationWorker (PeriodicTimer 60s) ── findDue() ────────────┘
   │  rollover sweep → findDue → por tarea: INotifierPort.SendAsync → mark reminder_sent_at
   │  EOD check (hora local >= EOD_TIME y día sin day_summaries) ── summary(today) ── Send ── INSERT day_summaries
```

## Schema (DDL)

```sql
-- src/Database/Migrations/001_init.sql
CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL,
  applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
CREATE TABLE categories (id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
CREATE TABLE tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL, day TEXT NOT NULL,          -- 'YYYY-MM-DD' wall-clock APP_TZ
  scheduled_time TEXT NOT NULL,                    -- 'HH:MM' wall-clock APP_TZ
  category_id INTEGER NOT NULL REFERENCES categories(id),
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN
    ('pending','in_progress','completed','not_done')),
  started_at TEXT, completed_at TEXT, duration_seconds INTEGER,  -- UTC ISO-8601
  day_closed_at TEXT, reminder_sent_at TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  CHECK ((status IN ('in_progress','completed')) = (started_at IS NOT NULL)),
  CHECK ((status = 'completed') = (completed_at IS NOT NULL)),
  CHECK ((status = 'not_done') = (day_closed_at IS NOT NULL)),
  CHECK (status != 'completed' OR (duration_seconds IS NOT NULL AND completed_at >= started_at)));
CREATE INDEX idx_tasks_day ON tasks(day, status, scheduled_time);
CREATE INDEX idx_tasks_category ON tasks(category_id);
CREATE INDEX idx_tasks_due ON tasks(day, scheduled_time)
  WHERE reminder_sent_at IS NULL AND status IN ('pending','in_progress');
CREATE TABLE day_summaries (day TEXT PRIMARY KEY, sent_at TEXT NOT NULL);
```

Runner: `PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;` y cada `.sql` pendiente en transacción, registrando versión.

## Interfaces / Contracts

```csharp
// src/Notifications/INotifierPort.cs
public interface INotifierPort { Task SendAsync(string text, CancellationToken ct = default); }
// findDue: SELECT * FROM tasks WHERE day=? AND scheduled_time<=? AND
//   status IN ('pending','in_progress') AND reminder_sent_at IS NULL;  (usa idx_tasks_due)
```

| Método | Ruta | Comportamiento |
|--------|------|----------------|
| POST | `/tasks` | 201; 400 categoría inexistente/validación |
| GET | `/tasks?day=` | Dashboard (default hoy) |
| GET | `/tasks/:id` | 200 / 404 |
| PATCH | `/tasks/:id` | title siempre; day/time solo `pending` (409); re-notificación CASE |
| POST | `/tasks/:id/start` | 200 idempotente (no-op si in_progress) |
| POST | `/tasks/:id/complete` | 200 `{duration}`; 409 si no in_progress |
| GET | `/categories` | Ordenadas por nombre |
| POST | `/categories` | 201; 409 duplicado (NOCASE) |
| PATCH | `/categories/:id` | 200; 409 duplicado |
| DELETE | `/categories/:id` | 204; 409 si hay tareas referenciadas |

DTOs validados con FluentValidation (validación global). Config: `APP_TZ`, `NOTIFY_ENABLED` (default `false`), `TELEGRAM_BOT_TOKEN`/`TELEGRAM_CHAT_ID` (fail-fast al boot si enabled), `EOD_TIME` (default `23:55`), `CATEGORIES`, `DB_PATH` (default `./data/app.db`) — leídos de appsettings.json + env vars (config nativa .NET). Resumen EOD: header con día + completadas con duración (formato `Xh Ym`/`Ym`) + en curso sin duración; 1 mensaje.

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `Program.cs` | Create | Bootstrap; DI; registro condicional de NotificationWorker |
| `src/Configuration/AppOptions.cs` | Create | Options + validación env fail-fast |
| `src/Common/TimeUtils.cs` | Create | `LocalParts`, `DayTimeToUtc`, `FormatDuration` (puras, testeables) |
| `src/Database/DatabaseService.cs` + `Migrations/001_init.sql` | Create | Conexión WAL, runner de migraciones, seed, Initialize |
| `src/Tasks/TaskRepository.cs`, `TaskService.cs`, `TasksController.cs`, `DTO/` | Create | SQL directo (Dapper); reglas de dominio; REST |
| `src/Categories/CategoryRepository.cs`, `CategoryService.cs`, `CategoriesController.cs`, `DTO/` | Create | CRUD categorías con checks FK |
| `src/Notifications/INotifierPort.cs`, `TelegramNotifier.cs`, `NotificationWorker.cs` | Create | Puerto, adaptador Telegram.Bot, ticker+EOD |
| `appsettings.json`, `.env.example`, `Roma.Api.csproj`, `Roma.Tests.csproj` | Create | Scaffold .NET |

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | TimeUtils (tz, duración), máquina de estados (transiciones, start no-op, edición bloqueada), NotificationWorker (dedup, agrupa due del minuto, EOD idempotente, retry no bloquea) | xUnit; repos fake + reloj/port fake |
| Integration | Repo sobre SQLite `:memory:` (Microsoft.Data.Sqlite): CHECKs de estado, seed (vacío → env; poblado → skip), delete categoría con FK, findDue, CASE re-notificación | conexión in-memory keep-alive + runner real |
| E2E | Flujos REST completos: crear→start→complete con duración, errores 400/404/409, dashboard por día | WebApplicationFactory + DB in-memory |

## Threat Matrix

N/A — sin routing, shell, subprocesos, automatización VCS/PR, clasificación de ejecutables ni integración de procesos. El bot es salida HTTPS vía librería (Telegram.Bot) dentro del mismo proceso; no se crean tareas de seguridad de procesos.

## Migration / Rollout

Sin migración de datos (greenfield; `001_init.sql` crea todo). Rollback: revertir commit; quitar `NOTIFY_ENABLED`/token detiene el bot sin tocar datos; borrar `data/app.db` resetea estado. Seed idempotente en cada boot.

## Open Questions

None — todos los gaps cerrados (seed, resumen EOD + idempotencia, transición `not_done`, reintento de envío, scheduler sin dependencia extra).
