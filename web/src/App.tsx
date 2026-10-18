import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, signInHref } from './api';
import { Activity } from './Activity';
import { AgentTokens } from './AgentTokens';
import { Board } from './Board';
import { HouseholdPanel } from './HouseholdPanel';
import { Leaderboard } from './Leaderboard';
import { useLiveUpdates } from './live';
import { OfflineBanner } from './Problem';
import { Shopping } from './Shopping';

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
  useLiveUpdates(hasHousehold ? households.data!.map((h) => h.id).join(',') : null);
  const view = useView();

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

      <OfflineBanner />

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

      {me.data && hasHousehold && (
        <nav className="nav">
          <a className={`tab${view === 'board' ? ' active' : ''}`} href="#board">
            Family
          </a>
          <a className={`tab${view === 'mine' ? ' active' : ''}`} href="#mine">
            Mine
          </a>
          <a className={`tab${view === 'shopping' ? ' active' : ''}`} href="#shopping">
            Shopping
          </a>
        </nav>
      )}

      {me.data && hasHousehold && view === 'shopping' && <Shopping />}
      {me.data && hasHousehold && view === 'board' && <Board board="family" />}
      {me.data && hasHousehold && view === 'mine' && <Board board="personal" />}
      {me.data && hasHousehold && view === 'board' && (
        <div className="side-by-side">
          <Leaderboard />
          <Activity />
        </div>
      )}
      {me.data && (view === 'board' || !hasHousehold) && <HouseholdPanel />}
      {me.data && hasHousehold && view === 'board' && <AgentTokens />}

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

type View = 'board' | 'mine' | 'shopping';

// Two pages don't justify a router library. The view lives in the URL hash, so /#shopping can be
// bookmarked or saved to a phone's home screen, and the back button works.
function useView(): View {
  const read = (): View =>
    window.location.hash === '#shopping' ? 'shopping' : window.location.hash === '#mine' ? 'mine' : 'board';
  const [view, setView] = useState<View>(read);
  useEffect(() => {
    const onChange = () => setView(read());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return view;
}
