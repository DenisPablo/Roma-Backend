CREATE TABLE IF NOT EXISTS schema_migrations (
  version INTEGER PRIMARY KEY,
  name TEXT NOT NULL,
  applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  day TEXT NOT NULL,
  scheduled_time TEXT NOT NULL,
  category_id INTEGER NOT NULL REFERENCES categories(id),
  kind TEXT NOT NULL DEFAULT 'task' CHECK (kind IN ('task','event')),
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','in_progress','completed','not_done')),
  started_at TEXT,
  completed_at TEXT,
  duration_seconds INTEGER,
  day_closed_at TEXT,
  reminder_sent_at TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  CHECK ((status IN ('in_progress','completed')) = (started_at IS NOT NULL)),
  CHECK ((status = 'completed') = (completed_at IS NOT NULL)),
  CHECK ((status = 'not_done') = (day_closed_at IS NOT NULL)),
  CHECK (status != 'completed' OR (duration_seconds IS NOT NULL AND completed_at >= started_at)),
  CHECK (kind != 'event' OR status = 'pending')
);

CREATE INDEX IF NOT EXISTS idx_tasks_day ON tasks(day, status, scheduled_time);
CREATE INDEX IF NOT EXISTS idx_tasks_category ON tasks(category_id);
CREATE INDEX IF NOT EXISTS idx_tasks_due ON tasks(day, scheduled_time)
  WHERE kind='task' AND reminder_sent_at IS NULL AND status IN ('pending','in_progress');

CREATE TABLE IF NOT EXISTS task_reminders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
  offset_minutes INTEGER NOT NULL,
  due_at TEXT NOT NULL,
  sent_at TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  UNIQUE (task_id, offset_minutes)
);

CREATE INDEX IF NOT EXISTS idx_task_reminders_due ON task_reminders(due_at) WHERE sent_at IS NULL;

CREATE TABLE IF NOT EXISTS day_summaries (
  day TEXT PRIMARY KEY,
  sent_at TEXT NOT NULL
);
