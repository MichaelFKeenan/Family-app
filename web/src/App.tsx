import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, signInHref } from './api';
import { Activity } from './Activity';
import { Board } from './Board';
import { HouseholdPanel } from './HouseholdPanel';

export function App() {
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: ['me'], queryFn: api.me });
  const households = useQuery({ queryKey: ['households'], queryFn: api.households, enabled: !!me.data });
  const health = useQuery({ queryKey: ['health'], queryFn: api.health });

  const signOut = useMutation({
    mutationFn: api.signOut,
    onSuccess: () => queryClient.clear(),
  });

  const hasHousehold = (households.data?.length ?? 0) > 0;

  return (
    <main className={`page${hasHousehold ? ' wide' : ''}`}>
      <header className="top">
        <h1>Family Board</h1>
        {me.data && (
          <div className="signed-in">
            {me.data.avatarUrl && <img className="avatar small" src={me.data.avatarUrl} alt="" referrerPolicy="no-referrer" />}
            <span className="name">{me.data.displayName}</span>
            <button className="link" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
              Sign out
            </button>
          </div>
        )}
      </header>

      {me.isPending && <section className="card">Checking sign-in…</section>}
      {me.isError && <section className="card bad">Couldn't check whether you're signed in.</section>}
      {me.data === null && (
        <section className="card">
          <p>Sign in to see your family's board.</p>
          <a className="button" href={signInHref()}>
            Sign in with Google
          </a>
        </section>
      )}

      {me.data && hasHousehold && <Board />}
      {me.data && hasHousehold && <Activity />}
      {me.data && <HouseholdPanel />}

      <p className="footer muted">
        {health.data
          ? `System: API reachable, database ${health.data.checks.postgres?.toLowerCase() ?? 'unknown'}`
          : health.isError
            ? 'System: API unreachable'
            : 'System: checking…'}
      </p>
    </main>
  );
}
