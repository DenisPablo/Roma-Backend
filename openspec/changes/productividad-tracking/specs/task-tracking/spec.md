# Task Tracking Specification

## Purpose

Tracks daily tasks with day/time scheduling, an explicit state machine, duration computed on completion, a managed category list (backend CRUD), and persistent day-close rollover. Domain of the NestJS `TaskModule`; no existing behavior — greenfield.

## Requirements

### Requirement: Task Creation

The system MUST allow creating a task with a title, a wall-clock day (`YYYY-MM-DD`, app timezone), a scheduled time (`HH:MM`), and an existing category. New tasks MUST start in `pending`.

#### Scenario: Create task with category

- GIVEN a valid title, day, time, and an existing category
- WHEN the user creates the task
- THEN the task is stored with status `pending`
- AND it appears in the day's dashboard

#### Scenario: Reject unknown category

- GIVEN a category that does not exist in the `categories` table
- WHEN the user creates the task
- THEN creation fails with a validation error

### Requirement: Category Management

The system MUST expose backend CRUD management for categories: a `categories` table with a foreign key from `tasks`, seeded at first boot from the `CATEGORIES` env var (comma-separated; defaults `Trabajo,Estudio,Salud,Personal,Hogar,Otros` when unset) so the UI has initial options, and endpoints to list, create, rename, and delete categories. Deleting a category still referenced by tasks MUST be rejected. The UI section to manage categories arrives with the frontend; the backend contract is ready in v1.

#### Scenario: List categories

- GIVEN seeded categories
- WHEN the API lists categories
- THEN all categories are returned ordered by name

#### Scenario: Create category

- GIVEN no existing category named `Dev`
- WHEN a category `Dev` is created
- THEN it is persisted and selectable by new tasks

#### Scenario: Reject duplicate category

- GIVEN an existing category `Hogar`
- WHEN creating `Hogar` again
- THEN creation fails with a validation error

#### Scenario: Rename category

- GIVEN an existing category
- WHEN its name is updated
- THEN the category keeps its id and all task references remain valid

#### Scenario: Delete category with tasks rejected

- GIVEN a category referenced by at least one task
- WHEN deleting it
- THEN deletion fails and the category remains

#### Scenario: Seed on first boot

- GIVEN an empty `categories` table and `CATEGORIES=Dev,Hogar`
- WHEN the app boots
- THEN the table contains exactly `Dev` and `Hogar`

#### Scenario: Seed skipped when populated

- GIVEN a `categories` table with existing rows
- WHEN the app boots
- THEN existing rows are kept and no reseed occurs

### Requirement: State Machine

The system MUST enforce the state machine with persistence guards (DB CHECKs) and these allowed transitions: `pending` → `in_progress` → `completed`; day-close rollover sets `pending` → `not_done`. `completed` and `not_done` MUST be terminal states. Starting an already `in_progress` task MUST be an idempotent no-op.

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

### Requirement: Day Close Rollover

At day close the system MUST set overdue `pending` tasks to `not_done`, persistent. Rolled-over tasks MUST NOT be re-notified, MUST NOT carry over to the next day, and MUST NOT receive a duration.

#### Scenario: Overdue task becomes not_done

- GIVEN a `pending` task scheduled yesterday and still pending at day close
- WHEN day close runs
- THEN its status becomes `not_done` with `day_closed_at` set

#### Scenario: Completed tasks unaffected

- GIVEN a task `completed` earlier in the day
- WHEN day close runs
- THEN its status remains `completed`

### Requirement: Duration on Completion

The system MUST compute duration only when completing: `completed_at − started_at` (UTC instants), formatted as hours/minutes. An `in_progress` task crossing midnight MUST keep its `started_at`; completing it later MUST yield the full elapsed span. Tasks without completion MUST have no duration.

#### Scenario: Same-day duration

- GIVEN a task started at 09:00 and completed at 09:35 UTC
- WHEN completion runs
- THEN duration is `35m`

#### Scenario: Duration across midnight

- GIVEN a task started at 23:50 UTC yesterday and completed at 00:20 UTC today
- WHEN completion runs
- THEN duration is `30m` and `started_at` is unchanged

### Requirement: Editing Rules

The system MUST allow editing the title at any time. Day and scheduled time MUST be editable only while `pending`; `started_at` and `completed_at` MUST be immutable once set. If an already-notified task's time is moved to a future moment, the system MUST clear `reminder_sent_at`.

#### Scenario: Edit pending task time

- GIVEN a `pending` task already notified (`reminder_sent_at` set)
- WHEN the user moves its time to a future moment
- THEN day/time are updated and `reminder_sent_at` is cleared

#### Scenario: Edit blocked when in progress

- GIVEN a task with status `in_progress`
- WHEN the user tries to change its day or time
- THEN the edit is rejected; only the title is editable
