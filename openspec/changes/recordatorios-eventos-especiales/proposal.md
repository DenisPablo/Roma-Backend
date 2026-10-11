# Proposal: Recordatorios para Eventos Especiales (Módulo 2)

## Intent

Support one-off events (exams, appointments) as milestones with MULTIPLE Telegram reminders and no state transitions. An event IS a task (Módulo 1): reuses day/time, category, dashboard.

## Scope

### In Scope
- `kind='event'` on tasks: milestone, status pinned `pending`, no transitions
- `task_reminders` table: per-row `offset_minutes`, absolute `due_at` (wall-clock APP_TZ), `sent_at` dedup; offset-0 row (at-time notice) included by default
- Global defaults `EVENT_REMINDER_OFFSETS` (CATEGORIES-style) with per-event snapshot + per-event override
- Time-edit recompute: all `due_at` recalculated; only future `sent_at` cleared (mirror of Módulo 1 CASE)
- Delivery policy P2 for overdue rows: send only MAX(due_at), mark `sent_at` in the same transaction; no catch-up
- Boot boundary rule (inherited ambiguity): rows overdue BEFORE boot marked `sent_at` WITHOUT sending; rows created AFTER boot send even if past — for tasks AND task_reminders
- Events excluded from EOD summary and rollover sweep; dashboard shows them as milestone in their day

### Out of Scope
- Recurring events; Telegram mutating commands; reminder CRUD UI (override via event edit only); catch-up after downtime

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `task-tracking`: `kind` column with domain CHECK (`kind != 'event' OR status = 'pending'`); `task_reminders` FK table; 409 guards on start/complete for events; sweep excludes events; editing rules extended to reminders
- `telegram-notifications`: `findDueReminders` (P2 — one overdue row per event); events excluded from `findDue` (double-send guard); events excluded from EOD; boot boundary rule; `EVENT_REMINDER_OFFSETS` defaults + snapshot

## Approach

Layered .NET scaffold (Roma.Domain / Roma.Application / Roma.Infrastructure / Roma.Api) adopted at Módulo 1 apply — migration 001 already contains `kind` + `task_reminders` (greenfield, no ALTER). Worker ticker adds `findDueReminders` after `findDue`; pure `TimeUtils` (`CalcDueAt` wall-clock parts, `ParseOffsets` d/h/m) testable. Service validates only `kind='event'` tasks have reminder rows (no FK-check in SQLite).

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Roma.Domain/TimeUtils.cs` | Modified | `ParseOffsets`, `CalcDueAt` wall-clock |
| `Roma.Domain/Entities/` | Modified | `Task.kind`, `EventReminder` entity |
| `Roma.Application/Services/` | Modified | CreateEvent, edit-recompute, P2 use case |
| `Roma.Infrastructure/Database/Migrations/001_init.sql` | Modified | `kind` + `task_reminders` in 001 |
| `Roma.Infrastructure/NotificationWorker.cs` | Modified | `findDueReminders`, boot boundary, exclusions |
| `Roma.Api/Controllers/` | Modified | event endpoints, 409 guards, `kind` exposed |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Double send if event enters `findDue` | Med | Exclude `kind='event'`; dedup per row |
| Boot boundary rule misimplemented | Med | Pure helper + unit tests |
| Forecast ~2k+ lines → review fatigue | High | Chained PRs (sdd-tasks) |
| Edit clears all `sent_at` → re-notify overdue | Low | Clear only future rows (CASE mirror) |

## Rollback Plan

Planning: git revert of the change folder. Implementation: migration `down` drops `task_reminders` + `kind`; removing `EVENT_REMINDER_OFFSETS` stops new defaults; worker exclusion off restores Módulo 1 behavior.

## Dependencies

- Módulo 1 implemented (layers, worker, Telegram bot); .NET SDK 10 LTS; `EVENT_REMINDER_OFFSETS` env (optional, defaults `-7d,-3d,-1d,-2h`)

## Success Criteria

- [ ] Event → N reminder rows (offset 0 included); overdue event → exactly 1 message (P2)
- [ ] No duplicate sends across restarts/edits/downtime (`sent_at` per row)
- [ ] Boot boundary rule covered by unit tests for tasks and reminders
- [ ] Events absent from EOD summary and rollover; dashboard shows milestone in its day
