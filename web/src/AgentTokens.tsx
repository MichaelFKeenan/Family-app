import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type AccessToken, type CreatedAccessToken } from './api';
import { LoadError } from './Problem';

const shortDate = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

/**
 * Personal access tokens, so an agent such as Claude Code can use the board as you (ADR-039).
 * Folded away by default: most visits are about chores, not this.
 */
export function AgentTokens() {
  const [open, setOpen] = useState(false);

  return (
    <details className="card agent-tokens" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>
        <h2>Agent tokens</h2>
      </summary>
      {open && <AgentTokensBody />}
    </details>
  );
}

function AgentTokensBody() {
  const tokens = useQuery({ queryKey: ['accessTokens'], queryFn: api.accessTokens });

  return (
    <>
      <p className="muted">
        A token lets an AI agent such as Claude Code read and change tasks and the shopping list as you, in this
        household. It can't invite people or make more tokens. Treat it like a password. It expires after 90 days.
      </p>
      <CreateToken />
      {tokens.isPending && <p>Loading tokens…</p>}
      {tokens.isError && <LoadError what="your tokens" onRetry={() => tokens.refetch()} />}
      {tokens.data && tokens.data.length > 0 && (
        <ul className="token-list">
          {tokens.data.map((token) => (
            <TokenRow key={token.id} token={token} />
          ))}
        </ul>
      )}
    </>
  );
}

function CreateToken() {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [created, setCreated] = useState<CreatedAccessToken | null>(null);
  const [copied, setCopied] = useState(false);

  const create = useMutation({
    mutationFn: api.createAccessToken,
    onSuccess: (token) => {
      setCreated(token);
      setCopied(false);
      setName('');
      queryClient.invalidateQueries({ queryKey: ['accessTokens'] });
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (name.trim()) create.mutate(name.trim());
  };

  const copy = async () => {
    if (!created) return;
    await navigator.clipboard.writeText(created.token);
    setCopied(true);
  };

  return (
    <>
      <form className="row" onSubmit={submit}>
        <input
          className="input"
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="What it's for, e.g. laptop Claude Code"
          maxLength={100}
          aria-label="Token name"
        />
        <button className="button" type="submit" disabled={create.isPending || !name.trim()}>
          Create
        </button>
      </form>
      {create.isError && <p className="bad">{create.error.message}</p>}
      {created && (
        <div className="new-token">
          <p className="good">
            Copy "{created.name}" now. You won't be able to see it again; if you lose it, revoke it and make a new one.
          </p>
          <div className="row">
            <input className="input mono" readOnly value={created.token} aria-label="New token" onFocus={(e) => e.target.select()} />
            <button className="button" onClick={copy}>
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
        </div>
      )}
    </>
  );
}

function TokenRow({ token }: { token: AccessToken }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const revoke = useMutation({
    mutationFn: () => api.revokeAccessToken(token.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accessTokens'] }),
  });

  const expired = new Date(token.expiresAt) <= new Date();
  const state = token.revokedAt
    ? `Revoked ${shortDate(token.revokedAt)}`
    : expired
      ? `Expired ${shortDate(token.expiresAt)}`
      : `Expires ${shortDate(token.expiresAt)}`;
  const active = !token.revokedAt && !expired;

  return (
    <li className={active ? undefined : 'inactive'}>
      <div className="token-text">
        <span>{token.name}</span>
        <span className="muted small">
          {state} · {token.lastUsedAt ? `last used ${shortDate(token.lastUsedAt)}` : 'never used'}
        </span>
      </div>
      {active && !confirming && (
        <button className="link" onClick={() => setConfirming(true)}>
          Revoke
        </button>
      )}
      {active && confirming && (
        <span className="confirm">
          <span className="small">Stop it working?</span>
          <button className="link danger" onClick={() => revoke.mutate()} disabled={revoke.isPending}>
            Revoke
          </button>
          <button className="link" onClick={() => setConfirming(false)}>
            Keep
          </button>
        </span>
      )}
      {revoke.isError && <span className="bad small">{revoke.error.message}</span>}
    </li>
  );
}
