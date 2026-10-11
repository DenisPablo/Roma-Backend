# Delta for Task Tracking

## ADDED Requirements

### Requirement: Event Creation

The system MUST support creating an event as a task with `kind='event'`, a wall-clock day and time, and an existing category. Events MUST start and remain pinned in `pending` (milestone, no transitions). On creation the system MUST snapshot the global reminder defaults (`EVENT_REMINDER_OFFSETS`) into per-event reminder rows, including an at-time notice (offset 0) by default; a per-event explicit reminder list MUST override the defaults. An explicit list with invalid or duplicate offsets MUST be rejected with a validation error.

#### Scenario: Create event with default reminders

- GIVEN `EVENT_REMINDER_OFFSETS=-7d,-3d,-1d,-2h` and no explicit list
- WHEN an event is created for a future day/time
- THEN the task is stored with `kind='event'` and status `pending`
- AND reminder rows exist for `-7d,-3d,-1d,-2h,0` with `due_at` computed from the event moment

#### Scenario: Per-event override

- GIVEN an event created with an explicit list `-1d,0`
- WHEN the event is created
- THEN only those reminder rows are created and the defaults are not applied

#### Scenario: Reject invalid offsets

- GIVEN an explicit list containing a duplicate or malformed offset
- WHEN the event is created
- THEN creation fails with a validation error

### Requirement: Event Reminder Persistence

The system MUST persist reminders in a `task_reminders` table with per-row `offset_minutes`, absolute wall-clock `due_at` (event moment + offset, app timezone), nullable `sent_at`, `UNIQUE(task_id, offset_minutes)`, and a partial index over pending `due_at` rows. Only `kind='event'` tasks MUST have reminder rows; the service MUST reject any attempt to attach reminder rows to a regular task.

#### Scenario: Reminder rows computed on creation

- GIVEN an event at `2026-09-01 10:00`
- WHEN reminder rows are created with offset `-120` minutes
- THEN the row has `due_at='2026-09-01 08:00'` and `sent_at` NULL

#### Scenario: Task reminders rejected

- GIVEN a regular task (`kind='task'`)
- WHEN reminder rows are attached to it
- THEN the operation is rejected with a validation error

## MODIFIED Requirements

### Requirement: State Machine

The system MUST enforce the state machine with persistence guards (DB CHECKs) and these allowed transitions: `pending` → `in_progress` → `completed`; day-close rollover sets `pending` → `not_done`. `completed` and `not_done` MUST be terminal states. Starting an already `in_progress` task MUST be an idempotent no-op. Events (`kind='event'`) MUST NOT transition: the DB MUST enforce `kind != 'event' OR status = 'pending'`, and start/complete on an event MUST be rejected.
(Previously: all tasks followed the same transitions; no event kind)

| From | To | Guard |
|------|----|-------|
| pending | in_progress | sets `started_at` (UTC now) |
| in_progress | completed | sets `completed_at` (UTC now) |
| pending | not_done | day-close only, sets `day_closed_at` |
| in_progress | completed | crossing a day keeps `started_at` |

#### Scenario: Start and complete

- GIVEN a `pending` task
- WHEN the user starts it
- THEN status becomes `in_progress` and `started_at` is set in UTC

#### Scenario: Re-start is a no-op

- GIVEN a task with status `in_progress`
- WHEN the user presses start again
- THEN `started_at` is unchanged and status stays `in_progress`

#### Scenario: No reverse transitions

- GIVEN a `completed` task
- WHEN a transition back to `pending` is attempted
- THEN the transition is rejected

#### Scenario: Event transitions rejected

- GIVEN an event with status `pending`
- WHEN start or complete is attempted
- THEN the request is rejected with a conflict error and status stays `pending`

### Requirement: Day Close Rollover

At day close the system MUST set overdue `pending` tasks to `not_done`, persistent. Rolled-over tasks MUST NOT be re-notified, MUST NOT carry over to the next day, and MUST NOT receive a duration. Events (`kind='event'`) MUST be excluded from the rollover sweep; their status MUST remain `pending`.
(Previously: all pending tasks were rolled over at day close)

#### Scenario: Overdue task becomes not_done

- GIVEN a `pending` task scheduled yesterday and still pending at day close
- WHEN day close runs
- THEN its status becomes `not_done` with `day_closed_at` set

#### Scenario: Completed tasks unaffected

- GIVEN a task `completed` earlier in the day
- WHEN day close runs
- THEN its status remains `completed`

#### Scenario: Event survives day close

- GIVEN an event whose day has passed while status is `pending`
- WHEN day close runs
- THEN its status remains `pending` and no `day_closed_at` is set

### Requirement: Editing Rules

The system MUST allow editing the title at any time. Day and scheduled time MUST be editable only while `pending`; `started_at` and `completed_at` MUST be immutable once set. If an already-notified task's time is moved to a future moment, the system MUST clear `reminder_sent_at`. When an event's day or time is edited, the system MUST recompute the `due_at` of ALL its reminder rows and MUST clear `sent_at` ONLY on rows whose new `due_at` is in the future.
(Previously: reminder clearing applied only to the single `reminder_sent_at` on tasks)

#### Scenario: Edit pending task time

- GIVEN a `pending` task already notified (`reminder_sent_at` set)
- WHEN the user moves its time to a future moment
- THEN day/time are updated and `reminder_sent_at` is cleared

#### Scenario: Edit blocked when in progress

- GIVEN a task with status `in_progress`
- WHEN the user tries to change its day or time
- THEN the edit is rejected; only the title is editable

#### Scenario: Edit event time recomputes reminders

- GIVEN an event with reminder rows, one already sent (`sent_at` set, past `due_at`)
- WHEN its time is moved to a future moment
- THEN all `due_at` are recomputed and only rows with future `due_at` have `sent_at` cleared
