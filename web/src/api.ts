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

export type TaskStatus = 'todo' | 'doing' | 'done';
export type Priority = 'low' | 'medium' | 'high' | 'urgent';

export type Task = {
  id: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: Priority;
  points: number;
  categoryId: string | null;
  assigneeId: string | null;
  dueOn: string | null; // yyyy-mm-dd
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  completedBy: string | null;
  version: number;
};

export type Category = { id: string; name: string; sortOrder: number };

export type TaskInput = {
  title: string;
  description?: string | null;
  priority?: Priority;
  points?: number;
  categoryId?: string | null;
  assigneeId?: string | null;
  dueOn?: string | null;
};

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
    request<{ outcome: 'joined' | 'alreadyMember'; householdId: string }>(
      `/api/households/invites/${encodeURIComponent(code)}/accept`,
      { method: 'POST' },
    ),

  tasks: () => request<Task[]>('/api/tasks'),
  createTask: (input: TaskInput) => request<Task>('/api/tasks', { method: 'POST', body: JSON.stringify(input) }),
  // A full edit; version is the one the form was opened with, so a stale save gets a 409 (ADR-014).
  updateTask: (id: string, input: Omit<TaskInput, 'assigneeId'> & { version: number }) =>
    request<Task>(`/api/tasks/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  assignTask: (id: string, assigneeId: string | null) =>
    request<Task>(`/api/tasks/${id}/assign`, { method: 'POST', body: JSON.stringify({ assigneeId }) }),
  moveTask: (id: string, action: 'start' | 'complete' | 'reopen') =>
    request<Task>(`/api/tasks/${id}/${action}`, { method: 'POST' }),
  deleteTask: (id: string) => request<void>(`/api/tasks/${id}`, { method: 'DELETE' }),
  categories: () => request<Category[]>('/api/tasks/categories'),
  createCategory: (name: string) =>
    request<Category>('/api/tasks/categories', { method: 'POST', body: JSON.stringify({ name }) }),

  health: async (): Promise<Health> => (await fetch('/api/health')).json(),
};

// Sign-in is a full page navigation to Google, so it is a link, not a fetch.
// returnUrl brings the user back to the same page, including any ?invite= code.
export function signInHref(): string {
  const here = window.location.pathname + window.location.search;
  return `/api/identity/login?returnUrl=${encodeURIComponent(here)}`;
}
