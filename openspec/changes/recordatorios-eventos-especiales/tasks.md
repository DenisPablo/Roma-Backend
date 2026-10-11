# Tasks: Recordatorios para Eventos Especiales (Módulo 2)

> Supersede tasks.md plano del M1: scaffold en capas + M1+M2 en 001 greenfield.

## Review Workload Forecast

Estimated changed lines: ~3,100–3,600 (M1+M2 juntos)
Delivery strategy: ask-on-risk

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Work Units

| Unit | Goal | PR | Test command | Runtime harness | Rollback |
|------|------|----|--------------|-----------------|----------|
| 1 | Scaffold + 001 greenfield + config | PR 1 | `dotnet build && dotnet test` | `dotnet run` boots | Revert scaffold |
| 2 | Dominio M1: tasks + categories | PR 2 | `dotnet test --filter "~Tasks"` | Boot + curl | Revert Application |
| 3 | Eventos M2: kind + reminders + API | PR 3 | `dotnet test --filter "~Events"` | curl POST kind=event | Revert Event* |
| 4 | Worker: boot-sweep + findDueReminders (P2) + EOD | PR 4 | `dotnet test --filter "~Notifications"` | NOTIFY_ENABLED=true | NOTIFY_ENABLED=false |

## Phase 1: Scaffold & Infra

- [ ] 1.1 Instalar .NET SDK 10 LTS (`dotnet --version`).
- [ ] 1.2 Scaffold Roma.Domain/Application/Infrastructure/Api/Tests (xUnit + WebApplicationFactory); NuGet Dapper, Sqlite, Telegram.Bot, FluentValidation.
- [ ] 1.3 AppOptions + appsettings: APP_TZ, NOTIFY_ENABLED, TELEGRAM_*, EOD_TIME, CATEGORIES, DB_PATH, EVENT_REMINDER_OFFSETS; fail-fast.
- [ ] 1.4 TimeUtils.cs: LocalParts, DayTimeToUtc, FormatDuration, ParseOffsets, CalcDueAt (DST-skip) + tests.
- [ ] 1.5 001_init.sql: DDL design — kind CHECK + event→pending CHECK, idx_tasks_due WHERE kind='task', task_reminders (UNIQUE, índice parcial), day_summaries.
- [ ] 1.6 DatabaseService.cs: WAL, runner migraciones, seed idempotente; runner integración `:memory:`.

## Phase 2: Dominio M1

- [ ] 2.1 RED state-machine: start, re-start no-op, complete, reverse rejected, rollover → not_done.
- [ ] 2.2 ITaskRepository + TaskRepository.cs: CRUD, findDue (kind='task'), sweep, CASE re-notificación.
- [ ] 2.3 TaskService.cs: transiciones, edición day/time solo pending (409), duración.
- [ ] 2.4 TasksController.cs + DTOs: REST /tasks, 400/404/409.
- [ ] 2.5 Categories (port, repo, service, controller, DTO): CRUD, dup 409, delete 409 FK.
- [ ] 2.6 E2E: create→start→complete, dashboard, 400/404/409.

## Phase 3: Eventos & Reminders

- [ ] 3.1 RED eventos: start/complete → 409, reminders en task → error, dup offsets → 400.
- [ ] 3.2 TaskReminder + IReminderRepository + ReminderRepository.cs: CreateForEventAsync, FindDueRemindersAsync (P2 MAX), MarkSentAsync (misma tx), RecomputeForEventAsync, MarkOverdueBeforeBootAsync.
- [ ] 3.3 EventService.cs: CreateEvent (snapshot, override, 0), EditEvent (recompute, clear futuras).
- [ ] 3.4 NotifyDueRemindersUseCase (P2): ≤1 msg/evento, misma tx, sin catch-up.
- [ ] 3.5 API: POST /tasks {kind,reminders?}, PATCH recompute, 409 eventos, GET kind.

## Phase 4: Worker & Notifications

- [ ] 4.1 Boot-sweep: created_at ≤ BootUtc y due ≤ now → sent sin enviar.
- [ ] 4.2 NotificationWorker.cs (60s): rollover → findDue → findDueReminders → EOD sin eventos; NOTIFY_ENABLED condicional.
- [ ] 4.3 TelegramNotifier.cs + INotifierPort: send-only, 1 reintento.

## Phase 5: Testing & Cleanup

- [ ] 5.1 Unit: P2 MAX-only, boot-boundary (2 escenarios), recompute, EOD sin eventos.
- [ ] 5.2 Integración `:memory:`: CHECKs 001, CASCADE, FindDueReminders P2, recompute, boot-sweep.
- [ ] 5.3 E2E: create defaults/override, 400 dup, 409 eventos, dashboard, EOD sin evento.
- [ ] 5.4 .env.example (+EVENT_REMINDER_OFFSETS); build+test, boot NOTIFY_ENABLED false/true.
