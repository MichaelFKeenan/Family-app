-- Append-only history of every domain event (ADR-005). Rows are never updated or deleted.
-- payload is the event as JSON, so the shape can vary per event type without schema changes.
CREATE TABLE platform.activity_events (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL,
    actor_id     uuid        NOT NULL,
    type         text        NOT NULL,
    payload      jsonb       NOT NULL,
    occurred_at  timestamptz NOT NULL
);

-- The feed: one household's latest events. UUIDv7 ids are time-ordered, so id works as a tiebreaker.
CREATE INDEX ix_activity_events_household_time ON platform.activity_events (household_id, occurred_at DESC, id DESC);
