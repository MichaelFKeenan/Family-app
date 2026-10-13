import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ApiError } from './api';
import { App } from './App';
import './index.css';

// Server state lives in TanStack Query. Refetch on window focus is on by default,
// which is our v1 stand-in for real-time updates (ADR-017).
const queryClient: QueryClient = new QueryClient({
  // A 401 on any call means the 30-day session ran out (ADR-022). Re-asking "who am I?" flips the
  // app to the sign-in screen, instead of every panel showing its own error.
  queryCache: new QueryCache({ onError: (error) => onSessionError(error) }),
  mutationCache: new MutationCache({ onError: (error) => onSessionError(error) }),
  defaultOptions: {
    queries: {
      // The default is three retries with back-off, which leaves a failing page on "Loading…" for
      // about seven seconds. Retry once, and never for 4xx: asking again won't change the answer.
      retry: (failureCount, error) => failureCount < 1 && !(error instanceof ApiError && error.status < 500),
    },
  },
});

function onSessionError(error: Error) {
  if (error instanceof ApiError && error.status === 401) {
    void queryClient.invalidateQueries({ queryKey: ['me'] });
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
