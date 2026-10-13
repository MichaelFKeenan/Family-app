import { useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, type Category, type Member, type Priority, type Task, type TaskStatus } from './api';

const COLUMNS: { status: TaskStatus; title: string }[] = [
  { status: 'todo', title: 'To do' },
  { status: 'doing', title: 'Doing' },
  { status: 'done', title: 'Done' },
];

const PRIORITIES: Priority[] = ['low', 'medium', 'high', 'urgent'];
const PRIORITY_RANK: Record<Priority, number> = { urgent: 0, high: 1, medium: 2, low: 3 };

type Lookups = { categories: Category[]; members: Member[] };

export function Board() {
  const tasks = useQuery({ queryKey: ['tasks'], queryFn: api.tasks });
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const members = useQuery({ queryKey: ['members'], queryFn: api.members });

  const [categoryFilter, setCategoryFilter] = useState('');
  const [assigneeFilter, setAssigneeFilter] = useState('');

  const lookups: Lookups = { categories: categories.data ?? [], members: members.data ?? [] };

  // Filtering is done here rather than via the API's query parameters: the whole board is
  // already loaded, so changing a filter is instant and costs no request.
  const visible = useMemo(
    () =>
      (tasks.data ?? []).filter(
        (task) =>
          (!categoryFilter || task.categoryId === categoryFilter) &&
          (!assigneeFilter ||
            (assigneeFilter === 'none' ? task.assigneeId === null : task.assigneeId === assigneeFilter)),
      ),
    [tasks.data, categoryFilter, assigneeFilter],
  );

  if (tasks.isPending) return <section className="card">Loading the board…</section>;
  if (tasks.isError) return <section className="card bad">Couldn't load the board.</section>;

  return (
    <section className="board">
      <QuickAdd lookups={lookups} />

      <div className="filters">
        <select className="input" value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)} aria-label="Filter by category">
          <option value="">All categories</option>
          {lookups.categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <select className="input" value={assigneeFilter} onChange={(e) => setAssigneeFilter(e.target.value)} aria-label="Filter by person">
          <option value="">Everyone</option>
          <option value="none">Unassigned</option>
          {lookups.members.map((m) => (
            <option key={m.userId} value={m.userId}>
              {m.displayName}
            </option>
          ))}
        </select>
      </div>

      <div className="columns">
        {COLUMNS.map((column) => {
          const inColumn = visible
            .filter((t) => t.status === column.status)
            .sort((a, b) =>
              column.status === 'done'
                ? (b.completedAt ?? '').localeCompare(a.completedAt ?? '')
                : PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority] ||
                  (a.dueOn ?? '9999').localeCompare(b.dueOn ?? '9999'),
            );
          return (
            <div className="column" key={column.status}>
              <h2>
                {column.title} <span className="muted">{inColumn.length}</span>
              </h2>
              {inColumn.length === 0 && <p className="muted empty">Nothing here.</p>}
              {inColumn.map((task) => (
                <TaskCard key={task.id} task={task} lookups={lookups} />
              ))}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function QuickAdd({ lookups }: { lookups: Lookups }) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState('');
  const [more, setMore] = useState(false);
  const [priority, setPriority] = useState<Priority>('medium');
  const [points, setPoints] = useState(0);
  const [categoryId, setCategoryId] = useState('');
  const [assigneeId, setAssigneeId] = useState('');
  const [dueOn, setDueOn] = useState('');

  const create = useMutation({
    mutationFn: api.createTask,
    onSuccess: () => {
      setTitle('');
      queryClient.invalidateQueries({ queryKey: ['tasks'] });
      queryClient.invalidateQueries({ queryKey: ['activity'] });
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!title.trim()) return;
    create.mutate({
      title: title.trim(),
      priority,
      points,
      categoryId: categoryId || null,
      assigneeId: assigneeId || null,
      dueOn: dueOn || null,
    });
  };

  return (
    <form className="card quick-add" onSubmit={submit}>
      <div className="row first">
        <input
          className="input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Add a task…"
          maxLength={200}
          aria-label="New task title"
        />
        <button className="button" type="submit" disabled={create.isPending || !title.trim()}>
          Add
        </button>
      </div>
      <button type="button" className="link" onClick={() => setMore(!more)}>
        {more ? 'Fewer options' : 'More options'}
      </button>
      {more && (
        <div className="grid">
          <label>
            Priority
            <select className="input" value={priority} onChange={(e) => setPriority(e.target.value as Priority)}>
              {PRIORITIES.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </label>
          <label>
            Points
            <input
              className="input"
              type="number"
              min={0}
              max={1000}
              value={points}
              onChange={(e) => setPoints(Number(e.target.value))}
            />
          </label>
          <label>
            Category
            <CategorySelect value={categoryId} onChange={setCategoryId} categories={lookups.categories} />
          </label>
          <label>
            Assignee
            <MemberSelect value={assigneeId} onChange={setAssigneeId} members={lookups.members} />
          </label>
          <label>
            Due
            <input className="input" type="date" value={dueOn} onChange={(e) => setDueOn(e.target.value)} />
          </label>
        </div>
      )}
      {create.isError && <p className="bad">{create.error.message}</p>}
    </form>
  );
}

function TaskCard({ task, lookups }: { task: Task; lookups: Lookups }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['tasks'] });
    queryClient.invalidateQueries({ queryKey: ['activity'] });
  };

  // On a 409 someone else got there first; reload so the card shows the truth.
  const move = useMutation({
    mutationFn: (action: 'start' | 'complete' | 'reopen') => api.moveTask(task.id, action),
    onSettled: refresh,
  });
  const remove = useMutation({ mutationFn: () => api.deleteTask(task.id), onSettled: refresh });

  const category = lookups.categories.find((c) => c.id === task.categoryId);
  const assignee = lookups.members.find((m) => m.userId === task.assigneeId);
  const error = move.error ?? remove.error;

  if (editing) return <EditTask task={task} lookups={lookups} onDone={() => setEditing(false)} />;

  return (
    <article className={`task priority-${task.priority}`}>
      <p className="task-title">{task.title}</p>
      {task.description && <p className="muted task-description">{task.description}</p>}
      <div className="chips">
        {(task.priority === 'high' || task.priority === 'urgent') && <span className="chip strong">{task.priority}</span>}
        {task.points > 0 && <span className="chip">{task.points} pts</span>}
        {category && <span className="chip">{category.name}</span>}
        {task.dueOn && <span className="chip">due {formatDate(task.dueOn)}</span>}
        <span className="chip">{assignee ? assignee.displayName : 'unassigned'}</span>
      </div>
      <div className="task-actions">
        {task.status === 'todo' && (
          <button className="button small secondary" onClick={() => move.mutate('start')} disabled={move.isPending}>
            Start
          </button>
        )}
        {task.status !== 'done' && (
          <button className="button small" onClick={() => move.mutate('complete')} disabled={move.isPending}>
            Done
          </button>
        )}
        {task.status !== 'todo' && (
          <button className="button small secondary" onClick={() => move.mutate('reopen')} disabled={move.isPending}>
            Reopen
          </button>
        )}
        <button className="link" onClick={() => setEditing(true)}>
          Edit
        </button>
        <button
          className="link danger"
          onClick={() => window.confirm(`Delete "${task.title}"?`) && remove.mutate()}
          disabled={remove.isPending}
        >
          Delete
        </button>
      </div>
      {error && <p className="bad">{errorText(error)}</p>}
    </article>
  );
}

function EditTask({ task, lookups, onDone }: { task: Task; lookups: Lookups; onDone: () => void }) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description ?? '');
  const [priority, setPriority] = useState<Priority>(task.priority);
  const [points, setPoints] = useState(task.points);
  const [categoryId, setCategoryId] = useState(task.categoryId ?? '');
  const [assigneeId, setAssigneeId] = useState(task.assigneeId ?? '');
  const [dueOn, setDueOn] = useState(task.dueOn ?? '');

  const save = useMutation({
    mutationFn: async () => {
      // The version this form was opened with. If someone saved in the meantime, the API
      // answers 409 instead of silently overwriting their change.
      const updated = await api.updateTask(task.id, {
        title,
        description: description || null,
        priority,
        points,
        categoryId: categoryId || null,
        dueOn: dueOn || null,
        version: task.version,
      });
      if ((assigneeId || null) !== task.assigneeId) await api.assignTask(task.id, assigneeId || null);
      return updated;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tasks'] });
      queryClient.invalidateQueries({ queryKey: ['activity'] });
      onDone();
    },
    onError: () => queryClient.invalidateQueries({ queryKey: ['tasks'] }),
  });

  return (
    <form
      className="task editing"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} aria-label="Title" />
      <textarea
        className="input"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        placeholder="Notes"
        rows={2}
        maxLength={2000}
        aria-label="Description"
      />
      <div className="grid">
        <label>
          Priority
          <select className="input" value={priority} onChange={(e) => setPriority(e.target.value as Priority)}>
            {PRIORITIES.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </select>
        </label>
        <label>
          Points
          <input className="input" type="number" min={0} max={1000} value={points} onChange={(e) => setPoints(Number(e.target.value))} />
        </label>
        <label>
          Category
          <CategorySelect value={categoryId} onChange={setCategoryId} categories={lookups.categories} />
        </label>
        <label>
          Assignee
          <MemberSelect value={assigneeId} onChange={setAssigneeId} members={lookups.members} />
        </label>
        <label>
          Due
          <input className="input" type="date" value={dueOn} onChange={(e) => setDueOn(e.target.value)} />
        </label>
      </div>
      <div className="task-actions">
        <button className="button small" type="submit" disabled={save.isPending || !title.trim()}>
          Save
        </button>
        <button className="link" type="button" onClick={onDone}>
          Cancel
        </button>
      </div>
      {save.isError && <p className="bad">{errorText(save.error)}</p>}
    </form>
  );
}

function CategorySelect(props: { value: string; onChange: (v: string) => void; categories: Category[] }) {
  return (
    <select className="input" value={props.value} onChange={(e) => props.onChange(e.target.value)}>
      <option value="">None</option>
      {props.categories.map((c) => (
        <option key={c.id} value={c.id}>
          {c.name}
        </option>
      ))}
    </select>
  );
}

function MemberSelect(props: { value: string; onChange: (v: string) => void; members: Member[] }) {
  return (
    <select className="input" value={props.value} onChange={(e) => props.onChange(e.target.value)}>
      <option value="">Unassigned</option>
      {props.members.map((m) => (
        <option key={m.userId} value={m.userId}>
          {m.displayName}
        </option>
      ))}
    </select>
  );
}

function errorText(error: Error): string {
  if (error instanceof ApiError && error.status === 409) return `${error.message} The board has been refreshed.`;
  return error.message;
}

function formatDate(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
}
