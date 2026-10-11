# Tasks: Productividad Tracking (Módulo 1)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,900–2,100 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | 3 PRs: 1→2→3 |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Scaffold ASP.NET Core + xUnit, DatabaseService, migraciones, config, TimeUtils | PR 1 | `dotnet build && dotnet test` | `dotnet run` boots, migra, seedea | Revert scaffold commit |
| 2 | Dominio: tasks + categories CRUD, state machine, rollover, duration | PR 2 | `dotnet test --filter "FullyQualifiedName~Tasks"` (y `~Categories`) | Boot + curl REST flow | Revert `src/Tasks/`+`src/Categories/` |
| 3 | Notificaciones: INotifierPort, TelegramNotifier, NotificationWorker, wiring | PR 3 | `dotnet test --filter "FullyQualifiedName~Notifications"` | `NOTIFY_ENABLED=true` + token, ticker real | `NOTIFY_ENABLED=false` apaga todo |

## Phase 1: Foundation — Scaffold & Infra

- [ ] 1.1 Install .NET SDK 10 LTS (prerequisito de máquina; verificar con `dotnet --version`).
- [ ] 1.2 Scaffold `dotnet new webapi` (Roma.Api) + `dotnet new xunit` (Roma.Tests) con WebApplicationFactory (`Microsoft.AspNetCore.Mvc.Testing`); NuGet: `Dapper`, `Microsoft.Data.Sqlite`, `Telegram.Bot`, `FluentValidation`; verify `dotnet build` + `dotnet test`.
- [ ] 1.3 Re-evaluate `strict_tdd` in `openspec/config.yaml` → `true`, set `test_command: "dotnet test"` once xUnit runs.
- [ ] 1.4 Create `src/Configuration/AppOptions.cs`: fail-fast env (APP_TZ, NOTIFY_ENABLED false, TELEGRAM_* si enabled, EOD_TIME 23:55, CATEGORIES, DB_PATH) + `appsettings.json`.
- [ ] 1.5 Create `src/Common/TimeUtils.cs` (LocalParts, DayTimeToUtc, FormatDuration) + unit tests (DST, cross-midnight).
- [ ] 1.6 Create `src/Database/Migrations/001_init.sql`: DDL design (CHECKs, bi-condicionales, idx_tasks_due, day_summaries).
- [ ] 1.7 Create `src/Database/DatabaseService.cs`: WAL, runner de migraciones, seed idempotente en `Initialize()`.
- [ ] 1.8 Integration test runner `:memory:` (Microsoft.Data.Sqlite): migraciones aplican, CHECKs rechazan, seed vacío/poblado.

## Phase 2: Core — Task & Category Domain

- [ ] 2.1 RED state-machine tests: start setea started_at, re-start no-op, complete con duración (día/cross-midnight), reverse rejected, rollover → not_done.
- [ ] 2.2 Create `src/Tasks/TaskRepository.cs`: Dapper CRUD, findDue, rollover sweep, CASE re-notificación.
- [ ] 2.3 Create `src/Tasks/TaskService.cs`: transiciones, edición day/time solo pending (409), duration en complete.
- [ ] 2.4 Create `src/Tasks/TasksController.cs` + `DTO/`: REST /tasks con FluentValidation; 400/404/409.
- [ ] 2.5 Create `src/Categories/` (repository, service, controller, DTO): list sorted, create 409 NOCASE dup, rename, delete 409 con tareas (FK).
- [ ] 2.6 E2E WebApplicationFactory `:memory:`: create→start→complete con duración, dashboard, 400/404/409.

## Phase 3: Notifications — Worker & Bot

- [ ] 3.1 Create `src/Notifications/INotifierPort.cs` + `TelegramNotifier.cs`: send-only, sin polling/webhook, 1 reintento, log.
- [ ] 3.2 RED NotificationWorker tests (reloj+port fake): dedup, agrupa due, sin catch-up, EOD idempotente, retry no bloquea.
- [ ] 3.3 Create `src/Notifications/NotificationWorker.cs`: BackgroundService + PeriodicTimer(60s) — ticker (findDue + sweep) y chequeo EOD (summary duraciones + INSERT day_summaries).
- [ ] 3.4 Wire en `Program.cs`: `AddHostedService<NotificationWorker>` solo si `NOTIFY_ENABLED=true` + fail-fast de credenciales.
- [ ] 3.5 Integration test re-notificación: editar hora futura limpia reminder_sent_at y re-notifica.

## Phase 4: Cleanup & Verification

- [ ] 4.1 Create `.env.example` documenting APP_TZ, NOTIFY_ENABLED, TELEGRAM_*, EOD_TIME, CATEGORIES, DB_PATH (y appsettings.json con defaults).
- [ ] 4.2 Final: `dotnet build`, `dotnet test`, boot con NOTIFY_ENABLED=false y true (fail-fast).
