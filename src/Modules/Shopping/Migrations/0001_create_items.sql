CREATE SCHEMA IF NOT EXISTS shopping;

-- One row per time something goes on the list. Ticking it off sets checked_at; the row stays,
-- so the table doubles as purchase history (ADR-010).
CREATE TABLE shopping.items (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL,
    name         text        NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
    -- Free text ("2", "500 g", "a big bag"): a family's quantities don't fit a number plus a unit.
    quantity     text        CHECK (length(quantity) <= 50),
    note         text        CHECK (length(note) <= 200),
    added_by     uuid        NOT NULL,
    added_at     timestamptz NOT NULL,
    checked_by   uuid,
    checked_at   timestamptz,

    CONSTRAINT ck_items_checked CHECK ((checked_at IS NULL) = (checked_by IS NULL))
);

-- An item can be on the active list only once per household, ignoring case. A partial index:
-- it covers unchecked rows only, so history can hold "Milk" hundreds of times (ADR-035).
-- It is also the conflict target for INSERT ... ON CONFLICT DO NOTHING when adding.
CREATE UNIQUE INDEX ux_items_active_name ON shopping.items (household_id, lower(name)) WHERE checked_at IS NULL;

-- "Recently ticked": one household's checked items, newest first.
CREATE INDEX ix_items_household_checked ON shopping.items (household_id, checked_at DESC) WHERE checked_at IS NOT NULL;
