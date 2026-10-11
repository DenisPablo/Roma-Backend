# Proposal: Productividad Tracking (Módulo 1)

## Intent

App personal de tracking: tareas con día/hora, estados explícitos, duración al completar, avisos por Telegram — sin herramientas genéricas. Módulo 1 greenfield.

## Scope

### In Scope
- Web app local: dashboard del día, CRUD de tareas, botones Iniciar/Completar
- Estados pending → in_progress → completed; pendientes vencidas → 'no realizada' (estado persistente) al cierre del día
- Duración = completed_at − started_at (UTC) en h/m; cruce de día conserva started_at
- Bot notify-only: aviso a la hora de cada tarea + resumen EOD
- SQLite (WAL) con migraciones SQL numeradas, acceso vía Dapper
- Categorías gestionables: tabla `categories` con CRUD backend (la sección de gestión llega con el frontend)

### Out of Scope
- Recurrencia; comandos del bot / mutación desde Telegram; pause/resume (`paused`); cola de reintentos
- Auth web (solo 127.0.0.1), multiusuario, push web

## Capabilities

### New Capabilities
- `task-tracking`: CRUD, máquina de estados con CHECKs, edición restringida (día/hora solo en pending), duración, cierre de día con 'no realizada' persistente, categorías gestionables
- `telegram-notifications`: bot send-only (sin polling/webhook), avisos con dedup `reminder_sent_at`, resumen EOD idempotente, avisos efímeros

### Modified Capabilities
None — proyecto nuevo, sin specs existentes.

## Approach

- ASP.NET Core monoproceso (C#): módulos `Tasks`, `Categories`, `Database` (Microsoft.Data.Sqlite + Dapper, WAL, migraciones al boot) y `Notifications` — registrado solo si `NOTIFY_ENABLED=true` — con `NotificationWorker` (BackgroundService + PeriodicTimer 60s) sobre el puerto `INotifierPort`, implementado por `TelegramNotifier` (Telegram.Bot send-only, sin polling/webhook)
- Minute ticker con PeriodicTimer de 60s: `findDue` en hora local + dedup `reminder_sent_at`; EOD configurable (default 23:55) chequed en el mismo worker, resumen idempotente vía `day_summaries`
- Bot send-only (Telegram.Bot); `NOTIFY_ENABLED=false` para solo-web; scheduling en wall-clock local (`APP_TZ`), timestamps en UTC ISO-8601
- Hora editada con reminder enviado → limpiar `reminder_sent_at`, re-notificar solo si nueva hora futura (CASE atómico en SQL, confirmado)
- Categorías: lista fija gestionable (tabla `categories` con CRUD backend; delete rechazado con tareas referenciadas; seed de defaults desde `CATEGORIES` solo si tabla vacía)

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Tasks/` | New | TaskService, TaskRepository (Dapper) |
| `src/Categories/` | New | CategoryService, CategoryRepository (Dapper) |
| `src/Database/` | New | DatabaseService, migraciones, conexión WAL |
| `src/Notifications/` | New | NotificationWorker, TelegramNotifier, INotifierPort |
| `Program.cs`, `appsettings.json` | New | Bootstrap, DI, wiring condicional |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Fallo de red Telegram | Med | log + reintento; EOD cubre |
| 'no realizada' amplía el CHECK de estados | Low | Definir transición y CHECK en spec (resuelto: migración 001 con bi-condicionales) |
| Hora inexistente por DST (02:30) | Low | Ignorar |
| SDK de .NET no instalado en la máquina | High (setup) | Instalar .NET 10 LTS antes de apply; tarea de bootstrap lo verifica |

## Rollback Plan

- Planeación: revertir artefactos del change vía git
- Implementación: migraciones reversibles (down); quitar `NOTIFY_ENABLED`/token detiene el bot sin tocar datos

## Dependencies

- `TELEGRAM_BOT_TOKEN` + `TELEGRAM_CHAT_ID` (env; fallar temprano si faltan); NuGet: `Dapper`, `Microsoft.Data.Sqlite`, `Telegram.Bot`; xUnit para tests
- Prerequisito de máquina: .NET SDK 10 LTS (instalar antes de apply)

## Success Criteria

- [ ] Propuesta aprobada; spec/design/tasks generados; decision gaps cerrados
- [ ] 2 capabilities (`task-tracking`, `telegram-notifications`) listas para sdd-spec
