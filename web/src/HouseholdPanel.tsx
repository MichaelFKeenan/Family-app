import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Household, type Invite } from './api';

// Invite links look like https://…/?invite=CODE. Read the code once, then strip it from the
// address bar so a refresh doesn't try to use it again.
function takeInviteCodeFromUrl(): string | null {
  const url = new URL(window.location.href);
  const code = url.searchParams.get('invite');
  if (code) {
    url.searchParams.delete('invite');
    window.history.replaceState(null, '', url.pathname + url.search + url.hash);
  }
  return code;
}

export function HouseholdPanel() {
  const queryClient = useQueryClient();
  const households = useQuery({ queryKey: ['households'], queryFn: api.households });

  const accept = useMutation({
    mutationFn: api.acceptInvite,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['households'] }),
  });

  // Accept an invite from the URL exactly once, even though React StrictMode runs effects twice.
  const handledInvite = useRef(false);
  useEffect(() => {
    if (handledInvite.current) return;
    handledInvite.current = true;
    const code = takeInviteCodeFromUrl();
    if (code) accept.mutate(code);
  }, [accept]);

  const inviteMessage = accept.isPending
    ? 'Joining household…'
    : accept.isError
      ? `Couldn't use that invite: ${accept.error.message}`
      : accept.data?.outcome === 'joined'
        ? 'You joined the household.'
        : accept.data?.outcome === 'alreadyMember'
          ? "You're already in that household."
          : null;

  if (households.isPending) return <section className="card">Loading your household…</section>;
  if (households.isError) return <section className="card bad">Couldn't load your households.</section>;

  // The UI assumes one household for now; the API already supports several (ADR-026).
  const household = households.data[0];

  return (
    <section className="card">
      {inviteMessage && <p className={accept.isError ? 'bad' : 'good'}>{inviteMessage}</p>}
      {household ? <HouseholdDetails household={household} /> : <CreateHousehold />}
    </section>
  );
}

function CreateHousehold() {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const create = useMutation({
    mutationFn: api.createHousehold,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['households'] }),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (name.trim()) create.mutate(name.trim());
  };

  return (
    <>
      <h2>Create your household</h2>
      <p className="muted">Or ask someone in your family for an invite link.</p>
      <form className="row" onSubmit={submit}>
        <input
          className="input"
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="e.g. The Keenans"
          maxLength={100}
          aria-label="Household name"
        />
        <button className="button" type="submit" disabled={create.isPending || !name.trim()}>
          Create
        </button>
      </form>
      {create.isError && <p className="bad">{create.error.message}</p>}
    </>
  );
}

function HouseholdDetails({ household }: { household: Household }) {
  const members = useQuery({ queryKey: ['members', household.id], queryFn: api.members });

  return (
    <>
      <h2>{household.name}</h2>
      {members.isPending && <p>Loading members…</p>}
      {members.data && (
        <ul className="members">
          {members.data.map((member) => (
            <li key={member.userId}>
              {member.avatarUrl ? (
                <img className="avatar small" src={member.avatarUrl} alt="" referrerPolicy="no-referrer" />
              ) : (
                <span className="avatar small placeholder" aria-hidden>
                  {member.displayName.slice(0, 1)}
                </span>
              )}
              <span>{member.displayName}</span>
              {member.role === 'admin' && <span className="tag">admin</span>}
            </li>
          ))}
        </ul>
      )}
      {household.role === 'admin' && <InviteSomeone />}
    </>
  );
}

function InviteSomeone() {
  const [copied, setCopied] = useState(false);
  const create = useMutation({ mutationFn: api.createInvite, onSuccess: () => setCopied(false) });
  const invite: Invite | undefined = create.data;
  const link = invite ? `${window.location.origin}/?invite=${invite.code}` : '';

  const copy = async () => {
    await navigator.clipboard.writeText(link);
    setCopied(true);
  };

  return (
    <div className="invite">
      <button className="button secondary" onClick={() => create.mutate()} disabled={create.isPending}>
        Invite someone
      </button>
      {create.isError && <p className="bad">{create.error.message}</p>}
      {invite && (
        <>
          <p className="muted">
            Send this link to one person. It works once and expires{' '}
            {new Date(invite.expiresAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })}.
            They also need to be on the app's Google test-user list.
          </p>
          <div className="row">
            <input className="input" readOnly value={link} aria-label="Invite link" />
            <button className="button" onClick={copy}>
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
