import { useQuery } from '@tanstack/react-query';
import { api, type ActivityEntry, type Member } from './api';
import { LoadError } from './Problem';

// Each entry is a domain event from the activity log (ADR-005), rendered as a sentence.
export function Activity() {
  const activity = useQuery({ queryKey: ['activity'], queryFn: () => api.activity(30), refetchInterval: 30_000 });
  const members = useQuery({ queryKey: ['members'], queryFn: api.members });

  // Points changes are already visible in the completion line ("… (5 pts)") and on the leaderboard.
  const entries = (activity.data ?? []).filter((entry) => !entry.type.startsWith('Points')).slice(0, 15);
  if (activity.isError)
    return (
      <section className="card">
        <h2>Recent activity</h2>
        <LoadError what="recent activity" onRetry={() => void activity.refetch()} />
      </section>
    );
  if (entries.length === 0) return null;

  return (
    <section className="card">
      <h2>Recent activity</h2>
      <ul className="activity">
        {entries.map((entry) => (
          <li key={entry.id}>
            <span>{describe(entry, members.data ?? [])}</span>
            <span className="muted when">{ago(entry.occurredAt)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function describe(entry: ActivityEntry, members: Member[]): string {
  const name = (id: unknown) => members.find((m) => m.userId === id)?.displayName ?? 'Someone';
  const actor = name(entry.actorId);
  const p = entry.payload;
  const title = `"${String(p.title ?? '')}"`;

  switch (entry.type) {
    case 'HouseholdCreated':
      return `${actor} created ${String(p.name)}`;
    case 'MemberJoined':
      return `${actor} joined the household`;
    case 'TaskCreated':
      return `${actor} added ${title}`;
    case 'TaskEdited':
      return `${actor} edited ${title}`;
    case 'TaskAssigned':
      return p.assigneeId ? `${actor} assigned ${title} to ${name(p.assigneeId)}` : `${actor} unassigned ${title}`;
    case 'TaskStarted':
      return `${actor} started ${title}`;
    case 'TaskCompleted':
      return `${actor} completed ${title}${Number(p.points) > 0 ? ` (${p.points} pts)` : ''}`;
    case 'TaskReopened':
      return `${actor} reopened ${title}`;
    case 'TaskDeleted':
      return `${actor} deleted ${title}`;
    case 'ShoppingItemAdded':
      return `${actor} put ${item(p)} on the shopping list`;
    case 'ShoppingItemChecked':
      return `${actor} ticked off ${item(p)}`;
    case 'ShoppingItemUnchecked':
      return `${actor} put ${item(p)} back on the list`;
    case 'ShoppingItemRemoved':
      return `${actor} took ${item(p)} off the list`;
    default:
      return `${actor}: ${entry.type}`;
  }
}

function item(p: Record<string, unknown>): string {
  return p.quantity ? `${String(p.quantity)} × ${String(p.name)}` : String(p.name ?? '');
}

function ago(iso: string): string {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}
