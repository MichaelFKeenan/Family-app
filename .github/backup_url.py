"""
Turns the app's .NET connection string (NEON_CONNECTION_STRING) into a postgres:// URL for
pg_dump, pointing at Neon's direct endpoint rather than the pooler (ADR-036).

pg_dump holds one long session and uses features a transaction-mode pooler (PgBouncer) doesn't
support, so Neon recommends the direct host: the same name without "-pooler".
"""
import os
import sys
from urllib.parse import quote

ALIASES = {
    "host": "host", "server": "host",
    "port": "port",
    "database": "database", "db": "database",
    "username": "user", "userid": "user", "user": "user", "uid": "user",
    "password": "password", "pwd": "password",
}

raw = os.environ.get("NEON_CONNECTION_STRING", "").strip()
settings = {}
for part in raw.split(";"):
    if "=" not in part:
        continue
    key, value = part.split("=", 1)
    name = ALIASES.get(key.strip().lower().replace(" ", ""))
    if name:
        settings[name] = value.strip()

missing = [k for k in ("host", "database", "user", "password") if not settings.get(k)]
if missing:
    sys.exit(f"NEON_CONNECTION_STRING is missing: {', '.join(missing)}")

host = settings["host"].replace("-pooler.", ".")
port = settings.get("port", "5432")
print(
    f"postgresql://{quote(settings['user'], safe='')}:{quote(settings['password'], safe='')}"
    f"@{host}:{port}/{quote(settings['database'], safe='')}?sslmode=require"
)
