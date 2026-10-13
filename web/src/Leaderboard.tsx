import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from './api';
import { LoadError } from './Problem';

type Period = 'week' | 'month' | 'all';

const PERIODS: { value: Period; label: string }[] = [
  { value: 'week', label: 'This week' },
  { value: 'month', label: 'This month' },
  { value: 'all', label: 'All time' },
];

// "This week" starts on Monday at midnight in the viewer's own time zone. The browser knows
// that zone; the server doesn't, so the browser works out the boundary and sends it (ADR-032).
function periodStart(period: Period): Date | null {
  const now = new Date();
  if (period === 'week') {
    const daysSinceMonday = (now.getDay() + 6) % 7;
    return new Date(now.getFullYear(), now.getMonth(), now.getDate() - daysSinceMonday);
  }
  if (period === 'month') return new Date(now.getFullYear(), now.getMonth(), 1);
  return null;
}

export function Leaderboard() {
  const [period, setPeriod] = useState<Period>('week');
  const members = useQuery({ queryKey: ['members'], queryFn: api.members });
  const leaderboard = useQuery({
    queryKey: ['leaderboard', period],
    queryFn: () => api.leaderboard(periodStart(period)),
    // Keep showing the last period's totals while the next loads, instead of flashing "Loading…".
    placeholderData: (previous) => previous,
  });

  // Everyone appears, including people on zero, ranked by points; ties share a rank.
  const totals = new Map((leaderboard.data ?? []).map((entry) => [entry.userId, entry.points]));
  const rows = (members.data ?? [])
    .map((member) => ({ member, points: totals.get(member.userId) ?? 0 }))
    .sort((a, b) => b.points - a.points || a.member.displayName.localeCompare(b.member.displayName));
  const rankOf = (points: number) => rows.findIndex((row) => row.points === points) + 1;

  return (
    <section className="card">
      <div className="leaderboard-head">
        <h2>Leaderboard</h2>
        <div className="tabs" role="tablist">
          {PERIODS.map((p) => (
            <button
              key={p.value}
              role="tab"
              aria-selected={period === p.value}
              className={`tab${period === p.value ? ' active' : ''}`}
              onClick={() => setPeriod(p.value)}
            >
              {p.label}
            </button>
          ))}
        </div>
      </div>

      {leaderboard.isError && <LoadError what="the leaderboard" onRetry={() => void leaderboard.refetch()} />}
      {leaderboard.isPending && <p className="muted">Loading…</p>}
      {/* Only with real totals: showing everyone on 0 while loading or after an error would be wrong. */}
      {leaderboard.isSuccess && (
      <ol className="leaderboard">
        {rows.map(({ member, points }) => {
          const rank = rankOf(points);
          return (
            <li key={member.userId} className={points > 0 && rank === 1 ? 'leader' : undefined}>
              <span className="rank">{points > 0 ? rank : '–'}</span>
              {member.avatarUrl ? (
                <img className="avatar small" src={member.avatarUrl} alt="" referrerPolicy="no-referrer" />
              ) : (
                <span className="avatar small placeholder" aria-hidden>
                  {member.displayName.slice(0, 1)}
                </span>
              )}
              <span className="who">{member.displayName}</span>
              <span className="score">{points} pts</span>
            </li>
          );
        })}
      </ol>
      )}
      {rows.every((row) => row.points === 0) && leaderboard.isSuccess && (
        <p className="muted">
          {period === 'all' ? 'No points yet.' : `No points ${period === 'week' ? 'this week' : 'this month'} yet.`} Complete a
          task with points to get on the board.
        </p>
      )}
    </section>
  );
}
