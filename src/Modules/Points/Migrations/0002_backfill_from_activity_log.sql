-- One-off backfill (ADR-033). Tasks completed between step 5 (activity log) and step 6 (points)
-- earned nothing, because the ledger didn't exist. The activity log recorded those completions,
-- so the ledger can be rebuilt from it: for each task, if its most recent completion-related
-- event is TaskCompleted, it is currently done, so award its points to the assignee, or to
-- whoever completed it if unassigned (ADR-016).
--
-- Two deliberate exceptions to the usual rules, both acceptable for a one-off script:
-- it reads the platform's activity_events table, and its ids are random UUIDv4 (Postgres 17
-- has no built-in UUIDv7).
INSERT INTO points.ledger (id, household_id, user_id, task_id, amount, reason, title, occurred_at)
SELECT gen_random_uuid(),
       latest.household_id,
       COALESCE((latest.payload ->> 'assigneeId')::uuid, latest.actor_id),
       (latest.payload ->> 'taskId')::uuid,
       (latest.payload ->> 'points')::int,
       'task_completed',
       latest.payload ->> 'title',
       latest.occurred_at
FROM (
    SELECT DISTINCT ON (payload ->> 'taskId') household_id, actor_id, type, payload, occurred_at
    FROM platform.activity_events
    WHERE type IN ('TaskCompleted', 'TaskReopened', 'TaskDeleted')
    ORDER BY payload ->> 'taskId', occurred_at DESC, id DESC
) AS latest
WHERE latest.type = 'TaskCompleted'
  AND (latest.payload ->> 'points')::int > 0;
