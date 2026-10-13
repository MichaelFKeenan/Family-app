#!/bin/sh
# Nightly logical backup of the Neon database to this server's disk (ADR-036).
#
# Neon's free plan can only restore to a point in the last 6 hours, so a mistake noticed the next
# day can't be undone there. These dumps cover the last KEEP_DAYS days. They live with a different
# provider from the data, so losing either Hetzner or Neon alone loses nothing.
#
# Restore (see README): docker compose run --rm backup pg_restore --dbname="<target url>" \
#     --clean --if-exists --no-owner /backups/family-YYYY-MM-DD.dump
set -u

KEEP_DAYS="${KEEP_DAYS:-14}"
DIR=/backups

backup() {
  day="$1"
  part="$DIR/.family-$day.dump.part"
  # Custom format: compressed, and pg_restore can restore single tables from it.
  if pg_dump --dbname="$DATABASE_URL" --format=custom --no-owner --no-privileges --file="$part" \
     && pg_restore --list "$part" >/dev/null; then
    # Rename only once the dump is complete and readable, so a family-*.dump file is always good.
    mv "$part" "$DIR/family-$day.dump"
    echo "$(date -u +%FT%TZ) backup ok: family-$day.dump ($(du -h "$DIR/family-$day.dump" | cut -f1))"
  else
    rm -f "$part"
    echo "$(date -u +%FT%TZ) backup FAILED for $day" >&2
  fi
  find "$DIR" -name 'family-*.dump' -mtime +"$KEEP_DAYS" -delete
}

# Checks every 10 minutes rather than sleeping until a fixed time, so a backup missed while the
# container was down (a deploy, a reboot) still happens that day. At most 3 attempts a day: a broken
# password retried all day would keep Neon awake and use up its free compute hours.
while true; do
  today=$(date -u +%F)
  attempts_file="$DIR/.attempts-$today"
  attempts=$(cat "$attempts_file" 2>/dev/null || echo 0)
  if [ "$(date -u +%H)" -ge 2 ] && [ ! -f "$DIR/family-$today.dump" ] && [ "$attempts" -lt 3 ]; then
    echo $((attempts + 1)) > "$attempts_file"
    backup "$today"
  fi
  find "$DIR" -name '.attempts-*' -mtime +2 -delete
  sleep 600
done
