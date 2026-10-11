# Delta for Telegram Notifications

## ADDED Requirements

### Requirement: Event Reminder Delivery

The scheduler MUST run a minute ticker that finds due event reminder rows (`sent_at IS NULL`, `due_at <= now` in app timezone) and notify each event. For events with MULTIPLE overdue rows the system MUST send exactly ONE message for the row with the MAX `due_at` and MUST mark that row's `sent_at` in the same transaction; other overdue rows MUST NOT be sent (no catch-up).

#### Scenario: One message for multiple overdue rows

- GIVEN an event with three reminder rows past due and `sent_at` NULL
- WHEN the ticker runs
- THEN exactly one message is sent, for the row with the latest `due_at`
- AND only that row's `sent_at` is set

#### Scenario: Per-row dedup

- GIVEN an event with one reminder row already sent and one row newly due
- WHEN the ticker runs
- THEN only the unsent row is processed and the sent row is ignored

#### Scenario: Independent events

- GIVEN two events each with overdue reminder rows
- WHEN the ticker runs
- THEN each event yields at most one message (two total)

### Requirement: Reminder Defaults Configuration

The system MUST read global reminder defaults from `EVENT_REMINDER_OFFSETS` (comma-separated, `d`/`h`/`m` suffixes; default `-7d,-3d,-1d,-2h` when unset) and MUST include an at-time notice (offset 0) by default. The defaults MUST be snapshotted into per-event rows at creation, so later changes MUST affect only newly created events.

#### Scenario: Defaults snapshot

- GIVEN `EVENT_REMINDER_OFFSETS=-1d,-2h`
- WHEN an event is created without an explicit list
- THEN its rows are fixed to those offsets plus offset 0, independent of later env changes

## MODIFIED Requirements

### Requirement: Due Task Reminders

The scheduler MUST run a minute ticker that finds due tasks — day equals today in app timezone, `scheduled_time <= now` local, `reminder_sent_at IS NULL`, status in `pending`/`in_progress`, `kind='task'` — notify each via the port, and set `reminder_sent_at` in the same transaction. Tasks due in the same minute MUST be sent together. Events MUST be excluded from this query; they are notified only through event reminder rows.
(Previously: all due tasks matched, including events)

#### Scenario: Notify due task once

- GIVEN a `pending` task scheduled for 09:00, now 09:01, `reminder_sent_at` NULL
- WHEN the ticker runs
- THEN a reminder is sent and `reminder_sent_at` is set in the same transaction

#### Scenario: No duplicate sends

- GIVEN a task already notified (`reminder_sent_at` set)
- WHEN the ticker runs again
- THEN no reminder is sent

#### Scenario: Event excluded from task query

- GIVEN an event due now with unsent reminder rows
- WHEN the task ticker runs
- THEN no task reminder is sent for it (prevents double send)

### Requirement: Ephemeral Delivery

Reminders MUST be ephemeral with a boot boundary: rows whose `due_at` (or task scheduled time) passed BEFORE the app booted MUST NOT be sent and MUST be marked `sent_at`/`reminder_sent_at` without sending; rows created AFTER boot with a past due time MUST be notified on the next tick. The rule applies to tasks and event reminder rows alike.
(Previously: ambiguous — downtime-missed and past-time-creation rules were separate)

#### Scenario: No catch-up after downtime

- GIVEN tasks due at 09:00 while the app was off, startup at 10:00
- WHEN the first ticker runs after startup
- THEN no late reminders are sent and their `reminder_sent_at` is marked

#### Scenario: Past-time creation notified immediately

- GIVEN a task created at 10:05 scheduled for 09:00, `reminder_sent_at` NULL
- WHEN the next ticker runs
- THEN the reminder is sent

#### Scenario: Overdue before boot marked without sending

- GIVEN an event reminder row due at 08:00 and the app boots at 09:00
- WHEN the first ticker runs
- THEN no message is sent and the row's `sent_at` is set

### Requirement: End-of-Day Summary

The system MUST run an EOD summary at a time configurable via env (default `23:55`, app timezone). It MUST be idempotent per day (day as primary key) and list completed tasks with duration and in-progress tasks without. Events MUST be excluded from the summary. It MUST NOT run when notifications are disabled.
(Previously: summary covered the whole day, including events)

#### Scenario: Summary with and without duration

- GIVEN one task completed today with a duration and one still `in_progress`
- WHEN the EOD cron fires
- THEN a summary covering both is sent and the day is recorded to prevent resend

#### Scenario: No duplicate summary

- GIVEN the day's summary was already recorded
- WHEN the EOD cron fires again
- THEN nothing is sent

#### Scenario: Event excluded from summary

- GIVEN a day with one completed task and one event
- WHEN the EOD cron fires
- THEN the summary lists only the completed task
