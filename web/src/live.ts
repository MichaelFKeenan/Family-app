import { useEffect } from 'react';
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';

// Live updates (ADR-042). The server never sends data, only which queries are now stale; the
// normal API calls fetch the rest, with all their checks. Topics are query-key roots: "shopping"
// marks ['shopping', 'active'] and ['shopping', 'checked'] stale.

/** Back off to every 30 s and never give up: phones lose signal, and every deploy restarts the server. */
const retryDelay = (attempt: number) => Math.min(30_000, 1_000 * 2 ** attempt);

/**
 * Keeps one live connection open while the user is in a household. `householdKey` changes when they
 * join or leave one; the hub only checks households on connect, so that reconnects.
 */
export function useLiveUpdates(householdKey: string | null) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!householdKey) return;

    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl('/api/live')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelay(previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('changed', (topics: string[]) => {
      for (const topic of topics) void queryClient.invalidateQueries({ queryKey: [topic] });
    });
    // Sent once the server has joined us to our groups, on every (re)connect. Anything that changed
    // before that, including while we were offline, would otherwise be missed.
    connection.on('ready', () => void queryClient.invalidateQueries());

    // Automatic reconnect only covers a connection that was once up; the first start retries here.
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const start = async (attempt: number) => {
      try {
        await connection.start();
      } catch {
        if (!stopped) timer = setTimeout(() => void start(attempt + 1), retryDelay(attempt));
      }
    };
    void start(0);

    return () => {
      stopped = true;
      clearTimeout(timer);
      void connection.stop();
    };
  }, [householdKey, queryClient]);
}
