CREATE TABLE IF NOT EXISTS workout_sessions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  date TEXT NOT NULL,
  target_muscle_group TEXT NOT NULL,
  rpe INTEGER NOT NULL CHECK (rpe BETWEEN 1 AND 10),
  feeling INTEGER NOT NULL CHECK (feeling BETWEEN 1 AND 5),
  recovery_score INTEGER NOT NULL CHECK (recovery_score BETWEEN 1 AND 5),
  notes TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE INDEX IF NOT EXISTS idx_workout_sessions_date ON workout_sessions(date DESC);
CREATE INDEX IF NOT EXISTS idx_workout_sessions_muscle ON workout_sessions(target_muscle_group);
