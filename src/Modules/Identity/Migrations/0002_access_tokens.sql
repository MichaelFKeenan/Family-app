-- Personal access tokens for agents (ADR-039).
--
-- Only a SHA-256 hash of the token is stored; the token itself is shown once, at creation.
-- A fast hash is safe here because the token is 32 random bytes, not a guessable password.
--
-- household_id is the one household the token may act in. This table is not household-owned
-- data, so it has no household query filter (an exception to ADR-009): a token is looked up by
-- its hash before anyone knows the household, because the token is what says which household.
-- No foreign key to households: modules never link across schemas.
CREATE TABLE identity.access_tokens (
    id           uuid        PRIMARY KEY,
    user_id      uuid        NOT NULL REFERENCES identity.users (id) ON DELETE CASCADE,
    household_id uuid        NOT NULL,
    name         text        NOT NULL,
    token_hash   bytea       NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    last_used_at timestamptz,
    revoked_at   timestamptz,
    CONSTRAINT uq_access_tokens_token_hash UNIQUE (token_hash)
);

CREATE INDEX ix_access_tokens_user_id ON identity.access_tokens (user_id);
