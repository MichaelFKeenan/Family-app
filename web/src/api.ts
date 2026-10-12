// Small typed wrappers over our REST API. The session cookie travels automatically because
// the API and the app share an origin (ADR-008), so there are no tokens to attach here.

export type Me = {
  id: string;
  displayName: string;
  email: string | null;
  avatarUrl: string | null;
};

export type Household = { id: string; name: string; role: 'admin' | 'member' };

export type Member = {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  role: 'admin' | 'member';
  joinedAt: string;
};

export type Invite = { code: string; expiresAt: string };

export type Health = { status: string; checks: Record<string, string> };

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (!response.ok) {
    // Errors come back as RFC 7807 problem details: { title, detail, status }.
    const problem = await response.json().catch(() => null);
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? response.statusText);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  // The cookie is HttpOnly, so asking the server is the only way to know who is signed in.
  me: async (): Promise<Me | null> => {
    try {
      return await request<Me>('/api/identity/me');
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return null;
      throw error;
    }
  },
  signOut: () => request<void>('/api/identity/logout', { method: 'POST' }),

  households: () => request<Household[]>('/api/households'),
  createHousehold: (name: string) =>
    request<Household>('/api/households', { method: 'POST', body: JSON.stringify({ name }) }),
  members: () => request<Member[]>('/api/households/members'),
  createInvite: () => request<Invite>('/api/households/invites', { method: 'POST' }),
  acceptInvite: (code: string) =>
    request<{ outcome: 'Joined' | 'AlreadyMember'; householdId: string }>(
      `/api/households/invites/${encodeURIComponent(code)}/accept`,
      { method: 'POST' },
    ),

  health: async (): Promise<Health> => (await fetch('/api/health')).json(),
};

// Sign-in is a full page navigation to Google, so it is a link, not a fetch.
// returnUrl brings the user back to the same page, including any ?invite= code.
export function signInHref(): string {
  const here = window.location.pathname + window.location.search;
  return `/api/identity/login?returnUrl=${encodeURIComponent(here)}`;
}
