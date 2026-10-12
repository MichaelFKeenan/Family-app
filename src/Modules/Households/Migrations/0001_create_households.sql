-- The tenant boundary (ADR-009). User ids come from the Identity module, so there are
-- deliberately no foreign keys to identity.users: modules own their tables and nothing else.
CREATE SCHEMA IF NOT EXISTS households;

CREATE TABLE households.households (
    id         uuid        PRIMARY KEY,
    name       text        NOT NULL,
    created_by uuid        NOT NULL,
    created_at timestamptz NOT NULL
);

CREATE TABLE households.memberships (
    household_id uuid        NOT NULL REFERENCES households.households (id) ON DELETE CASCADE,
    user_id      uuid        NOT NULL,
    role         text        NOT NULL CHECK (role IN ('admin', 'member')),
    joined_at    timestamptz NOT NULL,
    PRIMARY KEY (household_id, user_id)
);

-- "Which households is this user in?" runs on every household-scoped request (ADR-026).
CREATE INDEX ix_memberships_user_id ON households.memberships (user_id);

-- Only a SHA-256 hash of each invite code is stored (ADR-027): a leaked table holds no usable invites.
CREATE TABLE households.invites (
    id           uuid        PRIMARY KEY,
    household_id uuid        NOT NULL REFERENCES households.households (id) ON DELETE CASCADE,
    code_hash    bytea       NOT NULL UNIQUE,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    used_at      timestamptz,
    used_by      uuid
);
