import { useQuery } from '@tanstack/react-query';

type Health = {
  status: string;
  checks: Record<string, string>;
};

async function fetchHealth(): Promise<Health> {
  const response = await fetch('/api/health');
  // 503 still carries a JSON body describing which check failed.
  return (await response.json()) as Health;
}

export function App() {
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth });

  return (
    <main className="page">
      <h1>Family Board</h1>
      <p className="lead">Walking skeleton: the React app, the API and the database, wired end to end.</p>

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
