CREATE SCHEMA IF NOT EXISTS tasks;

-- Categories are per-household data, not an enum (ADR-011).
CREATE TABLE tasks.categories (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL,
    name         text        NOT NULL,
    sort_order   int         NOT NULL DEFAULT 0,
    created_at   timestamptz NOT NULL,
    CONSTRAINT uq_categories_household_name UNIQUE (household_id, name),
    -- Lets tasks reference (household_id, id), so the database itself refuses a task
    -- that points at another household's category.
    CONSTRAINT uq_categories_household_id UNIQUE (household_id, id)
);

CREATE TABLE tasks.tasks (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL,
    title        text        NOT NULL CHECK (length(title) BETWEEN 1 AND 200),
    description  text        CHECK (length(description) <= 2000),
    status       text        NOT NULL CHECK (status IN ('todo', 'doing', 'done')),
    priority     text        NOT NULL CHECK (priority IN ('low', 'medium', 'high', 'urgent')),
    points       int         NOT NULL DEFAULT 0 CHECK (points BETWEEN 0 AND 1000),
    category_id  uuid,
    assignee_id  uuid,
    due_on       date,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,
    completed_at timestamptz,
    completed_by uuid,
    -- Optimistic concurrency (ADR-014): every update bumps it, and an update that expected
    -- an older version matches no row.
    version      int         NOT NULL DEFAULT 1,

    -- Deleting a category un-categorises its tasks. SET NULL (category_id) clears only that
    -- column; clearing household_id too would break NOT NULL. Needs Postgres 15 or later.
    CONSTRAINT fk_tasks_category FOREIGN KEY (household_id, category_id)
        REFERENCES tasks.categories (household_id, id) ON DELETE SET NULL (category_id),

    -- A done task always records when and by whom; an open one never does.
    CONSTRAINT ck_tasks_completion CHECK ((status = 'done') = (completed_at IS NOT NULL AND completed_by IS NOT NULL))
);

-- The board query: one household's tasks, by status.
CREATE INDEX ix_tasks_household_status ON tasks.tasks (household_id, status);
