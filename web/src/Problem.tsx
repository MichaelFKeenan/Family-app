import { onlineManager } from '@tanstack/react-query';
import { useSyncExternalStore } from 'react';

/** A load failure with a way out: say what failed and offer to try again. */
export function LoadError({ what, onRetry }: { what: string; onRetry: () => void }) {
  return (
    <div className="load-error">
      <span className="bad">Couldn't load {what}.</span>
      <button className="link" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}

/**
 * Shown while the phone has no connection. TanStack Query pauses requests while offline and
 * sends paused changes (a tick in the shop, say) once the connection is back.
 */
export function OfflineBanner() {
  const online = useSyncExternalStore(
    (onChange) => onlineManager.subscribe(onChange),
    () => onlineManager.isOnline(),
  );
  if (online) return null;
  return <p className="offline">You're offline. Changes will be sent when you're back online.</p>;
}
