# Design: Event Special Reminders (Módulo 2)

## Technical Approach

Layered scaffold `Roma.Domain / Roma.Application / Roma.Infrastructure / Roma.Api` (+ `Roma.Tests`) adopted AT Módulo 1 apply — Módulo 1 is not yet implemented, so its flat layout (Program.cs + src/*) is superseded. Events are `kind='event'` tasks with per-event `task_reminders` rows, P2 overdue policy, boot-boundary rule, and `EVENT_REMINDER_OFFSETS` snapshot. Greenfield DB: `001_init.sql` ships M1 + M2 together (no ALTER).

## Architecture Decisions

| # | Decision | Tradeoff | Chosen |
|---|----------|----------|--------|
| 1 | Scaffold | Flat layout forces later re-architecture for dashboard/ML clients | **Layered, adopted at M1 apply** |
| 2 | Event marker | `is_event` bool cheaper but not extensible | **`kind TEXT CHECK (kind IN ('task','event'))` DEFAULT 'task'** + `CHECK (kind != 'event' OR status = 'pending')` |
| 3 | Reminder storage | JSON column vs rows (per-row dedup/queryability) | **`task_reminders` rows, `UNIQUE(task_id, offset_minutes)` + partial index `WHERE sent_at IS NULL`** |
| 4 | At-time notice | Two mechanisms vs one query path | **Offset-0 row by default; removable via override** |
| 5 | Defaults | Config table (runtime-edit, YAGNI v1) vs env snapshot | **`EVENT_REMINDER_OFFSETS` env (default `-7d,-3d,-1d,-2h`), snapshotted per event at creation** |
| 6 | Overdue policy | Send all vs exactly one | **P2: only MAX(due_at) row, `sent_at` set in same transaction, no catch-up** |
| 7 | Edit recompute | Clearing all `sent_at` re-notifies overdue | **Recompute all `due_at`; clear `sent_at` only where new `due_at` > now (M1 CASE mirror)** |
| 8 | Boot boundary | M1 ambiguity inherited | **Due-before-boot → marked sent without sending; created-after-boot → sent even if past (tasks AND reminders)** |
| 9 | Exclusions | Events in findDue/EOD/rollover → double-send, milestones rolled over | **`kind='task'` added to `idx_tasks_due`/findDue; new `findDueReminders`; EOD + rollover exclude events** |
| 10 | Channel | New bot/webhooks vs reuse | **Inherited M1: Telegram.Bot send-only, `NOTIFY_ENABLED` gate, PeriodicTimer 60s** |

## Layer Mapping (M1 → layers; M2 additions)

| Layer | Módulo 1 pieces (placed at apply) | Módulo 2 additions |
|-------|-----------------------------------|--------------------|
| `Roma.Domain` | `Task`, `Category` entities; `TimeUtils` (`LocalParts`, `DayTimeToUtc`, `FormatDuration`); state machine rules | `TaskKind`; `TaskReminder`; `TimeUtils.ParseOffsets`, `CalcDueAt` (wall-clock); pure P2 + boot-boundary predicates |
| `Roma.Application` | `TaskService`, `CategoryService`, `NotifyDueUseCase`, `EodSummaryUseCase`; ports `ITaskRepository`, `ICategoryRepository`, `INotifierPort`, `IClock` | `CreateEvent` (snapshot/override + validation), `EditEvent` (recompute + clear-future), `NotifyDueRemindersUseCase` (P2); port `IReminderRepository` |
| `Roma.Infrastructure` | `DatabaseService` + migrations, Dapper `TaskRepository`/`CategoryRepository`, `TelegramNotifier`, `NotificationWorker` (sweep → findDue → EOD) | 001 gains `kind` + `task_reminders`; `ReminderRepository`; worker adds boot-sweep + `findDueReminders`; sweeps/findDue/EOD exclude events; `AppOptions.EVENT_REMINDER_OFFSETS` |
| `Roma.Api` | Controllers, DTOs, FluentValidation, `Program.cs` DI, `AppOptions` | `kind` in DTOs/dashboard; `POST /tasks` accepts `kind` + `reminders`; PATCH recompute; 409 on event start/complete |

## Data Flow

```
Api ──► Use-cases ──► Domain rules (CalcDueAt, P2, boot) ──► Dapper repos ──► SQLite (WAL)
Worker 60s: boot-sweep (mark pre-boot due as sent) → rollover (kind='task')
  → findDue (kind='task') → findDueReminders (P2, ≤1 msg/event) → EOD (excludes events)
```

## Migration DDL (`Roma.Infrastructure/Database/Migrations/001_init.sql`)

```sql
CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL,
  applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
CREATE TABLE categories (id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
CREATE TABLE tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL, day TEXT NOT NULL, scheduled_time TEXT NOT NULL,
  category_id INTEGER NOT NULL REFERENCES categories(id),
  kind TEXT NOT NULL DEFAULT 'task' CHECK (kind IN ('task','event')),
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','in_progress','completed','not_done')),
  started_at TEXT, completed_at TEXT, duration_seconds INTEGER,
  day_closed_at TEXT, reminder_sent_at TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  CHECK ((status IN ('in_progress','completed')) = (started_at IS NOT NULL)),
  CHECK ((status = 'completed') = (completed_at IS NOT NULL)),
  CHECK ((status = 'not_done') = (day_closed_at IS NOT NULL)),
  CHECK (status != 'completed' OR (duration_seconds IS NOT NULL AND completed_at >= started_at)),
  CHECK (kind != 'event' OR status = 'pending'));
CREATE INDEX idx_tasks_day ON tasks(day, status, scheduled_time);
CREATE INDEX idx_tasks_category ON tasks(category_id);
CREATE INDEX idx_tasks_due ON tasks(day, scheduled_time)
  WHERE kind='task' AND reminder_sent_at IS NULL AND status IN ('pending','in_progress');
CREATE TABLE task_reminders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
  offset_minutes INTEGER NOT NULL, due_at TEXT NOT NULL, sent_at TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  UNIQUE (task_id, offset_minutes));
CREATE INDEX idx_task_reminders_due ON task_reminders(due_at) WHERE sent_at IS NULL;
CREATE TABLE day_summaries (day TEXT PRIMARY KEY, sent_at TEXT NOT NULL);
```

## Interfaces / Contracts

```csharp
int[]  ParseOffsets(string spec);                   // "-7d,-3d,-1d,-2h" → [-10080,-4320,-1440,-120]; dupes/malformed → error
string CalcDueAt(LocalParts moment, int offsetMin); // wall-clock parts; DST-nonexistent hour → skip (M1 precedent)
interface IClock { DateTimeOffset NowUtc; string NowLocal(); DateTimeOffset BootUtc; } // BootUtc captured at worker start
interface IReminderRepository {
  Task CreateForEventAsync(int taskId, int[] offsets, string eventMoment, CancellationToken ct);
  Task<IReadOnlyList<TaskReminder>> FindDueRemindersAsync(string nowLocal, CancellationToken ct); // unsent, due<=now, MAX(due_at) per event (P2)
  Task MarkSentAsync(int reminderId, CancellationToken ct);                    // same transaction as send
  Task RecomputeForEventAsync(int taskId, string newMoment, string nowLocal, CancellationToken ct); // clear sent_at only where new due_at > now
  Task MarkOverdueBeforeBootAsync(string bootLocal, DateTimeOffset bootUtc, CancellationToken ct);  // boot-sweep: mark, no send
}
```

Boot-sweep runs once at worker start: rows with `created_at <= bootUtc` (UTC) AND due moment `<= bootLocal` get `sent_at`/`reminder_sent_at` set WITHOUT sending; rows created after boot (or pre-existing with `due_at > boot`) stay eligible and send on the next tick. `created_at` (UTC) is the discriminator — both spec scenarios (downtime-missed vs past-time-creation) pin those two cases.

API: `POST /tasks` `{kind:"event", reminders:["-1d","0"]?}` (defaults when list absent) → 201 + rows; `PATCH /tasks/:id` (event → recompute `due_at`, clear only future `sent_at`); `POST /tasks/:id/start|complete` → 409 for events; `GET /tasks?day=` exposes `kind`. Only `kind='event'` may carry reminder rows (service validation — SQLite lacks cross-table FK checks).

## Testing Strategy

| Layer | What | How |
|-------|------|-----|
| Unit | `ParseOffsets` (dup/malformed), `CalcDueAt` (DST, offset 0), P2 MAX-only, boot boundary (both scenarios), event transitions rejected | xUnit; fake repos/clock/port |
| Integration | 001 CHECKs (`kind != 'event' OR status='pending'`, CASCADE), `FindDueReminders` P2, recompute clears only future, boot-sweep | SQLite `:memory:` + real migration runner |
| E2E | Create event (defaults/override), 400 dup offsets, 409 start/complete, dashboard shows event, EOD excludes event | WebApplicationFactory |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary (worker is an in-process BackgroundService; Telegram is HTTPS-out via library). Precedent: Módulo 1.

## Migration / Rollout

Greenfield — no data migration; 001 ships both modules. Rollback: `down` drops `task_reminders` + `kind`; unset `EVENT_REMINDER_OFFSETS` stops new defaults; worker exclusions off restore M1 behavior. Delivery: chained PRs (M1 scaffold + features → M2); forecast in sdd-tasks (review budget 400, `ask-on-risk`).

## Open Questions

None — all 10 decisions closed in proposal/spec.
