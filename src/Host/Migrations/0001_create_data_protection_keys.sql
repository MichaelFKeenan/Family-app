-- Keys ASP.NET Core uses to encrypt the sign-in cookie (ADR-023).
-- Stored here so every machine and every deploy shares them; otherwise each
-- restart would generate new keys and sign everyone out.
CREATE TABLE platform.data_protection_keys (
    id            uuid        PRIMARY KEY,
    friendly_name text,
    xml           text        NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);
