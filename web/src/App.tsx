import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

type Health = {
  status: string;
  checks: Record<string, string>;
};

type Me = {
  id: string;
  displayName: string;
  email: string | null;
  avatarUrl: string | null;
};

async function fetchHealth(): Promise<Health> {
  const response = await fetch('/api/health');
  // 503 still carries a JSON body describing which check failed.
  return (await response.json()) as Health;
}

// The session cookie is HttpOnly, so JavaScript can't see it. The only way to know whether
// we're signed in is to ask the server (ADR-008).
async function fetchMe(): Promise<Me | null> {
  const response = await fetch('/api/identity/me');
  if (response.status === 401) return null;
  if (!response.ok) throw new Error(`Unexpected ${response.status} from /api/identity/me`);
  return (await response.json()) as Me;
}

export function App() {
  const queryClient = useQueryClient();
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth });
  const me = useQuery({ queryKey: ['me'], queryFn: fetchMe });

  const signOut = useMutation({
    mutationFn: () => fetch('/api/identity/logout', { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
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
            {/* A full page navigation, not a fetch: the browser has to visit Google. */}
            <a className="button" href="/api/identity/login?returnUrl=/">
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
              <p className="name">Signed in as {me.data.displayName}</p>
              {me.data.email && <p className="muted">{me.data.email}</p>}
            </div>
            <button className="button secondary" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
              Sign out
            </button>
          </div>
        )}
      </section>

      <section className="card" aria-live="polite">
        <h2>System status</h2>
        {health.isPending && <p>Checking…</p>}
        {health.isError && <p className="bad">The API is unreachable.</p>}
        {health.data && (
          <ul>
            <li>
              API: <span className="good">reachable</span>
            </li>
            {Object.entries(health.data.checks).map(([name, status]) => (
              <li key={name}>
                {name}: <span className={status === 'Healthy' ? 'good' : 'bad'}>{status}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}
