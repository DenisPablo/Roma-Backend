# Telegram Notifications Specification

## Purpose

Send-only Telegram notifications for the task domain: due-task reminders and an end-of-day summary. Side-effect-only adapter (`TelegramNotifier`) behind a `NotifierPort`; never receives commands, never mutates data. Greenfield — no existing behavior.

## Requirements

### Requirement: Notify-Only Bot

The bot MUST be send-only: it MUST NOT use polling or webhooks, MUST NOT handle commands, and MUST NOT mutate task data. With `NOTIFY_ENABLED=false` the module MUST NOT start. With notifications enabled and `TELEGRAM_BOT_TOKEN` or `TELEGRAM_CHAT_ID` missing, startup MUST fail early.

#### Scenario: Disabled by env

- GIVEN `NOTIFY_ENABLED=false` and no token
- WHEN the app boots
- THEN the notification module does not start

#### Scenario: Missing credentials

- GIVEN `NOTIFY_ENABLED=true` and no `TELEGRAM_BOT_TOKEN`
- WHEN the app boots
- THEN startup fails with a clear error

### Requirement: Due Task Reminders

The scheduler MUST run a minute ticker that finds due tasks — day equals today in app timezone, `scheduled_time <= now` local, `reminder_sent_at IS NULL`, status in `pending`/`in_progress` — notify each via the port, and set `reminder_sent_at` in the same transaction. Tasks due in the same minute MUST be sent together.

#### Scenario: Notify due task once

- GIVEN a `pending` task scheduled for 09:00, now 09:01, `reminder_sent_at` NULL
- WHEN the ticker runs
- THEN a reminder is sent and `reminder_sent_at` is set in the same transaction

#### Scenario: No duplicate sends

- GIVEN a task already notified (`reminder_sent_at` set)
- WHEN the ticker runs again
- THEN no reminder is sent

### Requirement: Re-notification on Reschedule

When an already-notified task is edited to a future time (clearing `reminder_sent_at`), the system MUST notify it again when the new time arrives.

#### Scenario: Notify at new time

- GIVEN a task notified at 09:00 whose time is moved to 15:00
- WHEN 15:00 arrives and the ticker runs
- THEN a new reminder is sent and `reminder_sent_at` updates to the new send time

### Requirement: Ephemeral Delivery

Reminders MUST be ephemeral: if the app was not running, missed due tasks MUST NOT be re-sent on startup. A task created with a past time MUST be notified on the next tick.

#### Scenario: No catch-up after downtime

- GIVEN tasks due at 09:00 while the app was off, startup at 10:00
- WHEN the first ticker runs after startup
- THEN no late reminders are sent

#### Scenario: Past-time creation notified immediately

- GIVEN a task created at 10:05 scheduled for 09:00, `reminder_sent_at` NULL
- WHEN the next ticker runs
- THEN the reminder is sent

### Requirement: End-of-Day Summary

The system MUST run an EOD summary at a time configurable via env (default `23:55`, app timezone). It MUST be idempotent per day (day as primary key) and list completed tasks with duration and in-progress tasks without. It MUST NOT run when notifications are disabled.

#### Scenario: Summary with and without duration

- GIVEN one task completed today with a duration and one still `in_progress`
- WHEN the EOD cron fires
- THEN a summary covering both is sent and the day is recorded to prevent resend

#### Scenario: No duplicate summary

- GIVEN the day's summary was already recorded
- WHEN the EOD cron fires again
- THEN nothing is sent

### Requirement: Delivery Failure Handling

On Telegram network failure the system MUST log the error and retry once, with no persistent queue. A failed reminder MUST NOT block the remaining due tasks, and the EOD summary MUST still cover the day.

#### Scenario: Single retry without blocking

- GIVEN `sendMessage` fails with a network error while other tasks are due
- WHEN the notifier sends
- THEN one retry is attempted, the error is logged, and the other tasks are still notified
