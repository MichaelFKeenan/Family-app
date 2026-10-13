CREATE SCHEMA IF NOT EXISTS points;

-- Append-only (ADR-006): rows are only ever inserted. Reopening a task doesn't delete its award;
-- it adds a negative entry. Totals for any period are a SUM over this table.
CREATE TABLE points.ledger (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL,
    user_id      uuid        NOT NULL,
    task_id      uuid,
    amount       int         NOT NULL CHECK (amount <> 0),
    reason       text        NOT NULL CHECK (reason IN ('task_completed', 'task_reopened', 'task_deleted')),
    -- The task title when the entry was made, so history stays readable after a rename or delete.
    title        text,
    occurred_at  timestamptz NOT NULL
);

-- Leaderboard: one household's entries in a time range.
CREATE INDEX ix_ledger_household_time ON points.ledger (household_id, occurred_at);

-- Reversal: what is still owed for one task, per person.
CREATE INDEX ix_ledger_household_task ON points.ledger (household_id, task_id);
