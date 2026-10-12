import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, signInHref } from './api';
import { HouseholdPanel } from './HouseholdPanel';

export function App() {
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: ['me'], queryFn: api.me });
  const health = useQuery({ queryKey: ['health'], queryFn: api.health });

  const signOut = useMutation({
    mutationFn: api.signOut,
    onSuccess: () => queryClient.clear(),
  });

  return (
    <main className="page">
      <h1>Family Board</h1>

      <section className="card">
        {me.isPending && <p>Checking sign-in…</p>}
        {me.isError && <p className="bad">Couldn't check whether you're signed in.</p>}
        {me.data === null && (
          <>
            <p>Sign in to see your family's board.</p>
            <a className="button" href={signInHref()}>
              Sign in with Google
            </a>
          </>
        )}
        {me.data && (
          <div className="signed-in">
            {me.data.avatarUrl && (
              <img className="avatar" src={me.data.avatarUrl} alt="" referrerPolicy="no-referrer" />
            )}
            <div>
              <p className="name">{me.data.displayName}</p>
              {me.data.email && <p className="muted">{me.data.email}</p>}
            </div>
            <button className="button secondary" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
              Sign out
            </button>
          </div>
        )}
      </section>

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
