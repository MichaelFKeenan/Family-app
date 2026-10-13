-- Personal tasks (ADR-037): owner_id NULL is a family task; set, the task is on that person's
-- personal board and only they can see it. Still household-scoped, like every task.
ALTER TABLE tasks.tasks ADD COLUMN owner_id uuid;

-- A personal task can be assigned only to its owner.
ALTER TABLE tasks.tasks ADD CONSTRAINT ck_tasks_personal_assignee
    CHECK (owner_id IS NULL OR assignee_id IS NULL OR assignee_id = owner_id);

-- The personal board query: one household, one owner.
CREATE INDEX ix_tasks_household_owner ON tasks.tasks (household_id, owner_id) WHERE owner_id IS NOT NULL;
