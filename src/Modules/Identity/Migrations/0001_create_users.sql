-- Users are profiles only; how someone logs in lives in user_identities (ADR-007).
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE identity.users (
    id           uuid        PRIMARY KEY,
    display_name text        NOT NULL,
    email        text,
    avatar_url   text,
    created_at   timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL
);

-- One row per login method. Keyed on the provider's stable subject id, never the email,
-- because emails can change and can be reused.
CREATE TABLE identity.user_identities (
    id               uuid        PRIMARY KEY,
    user_id          uuid        NOT NULL REFERENCES identity.users (id) ON DELETE CASCADE,
    provider         text        NOT NULL,
    provider_subject text        NOT NULL,
    email            text,
    created_at       timestamptz NOT NULL,
    CONSTRAINT uq_user_identities_provider_subject UNIQUE (provider, provider_subject)
);

CREATE INDEX ix_user_identities_user_id ON identity.user_identities (user_id);
