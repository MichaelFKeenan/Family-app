import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, type Member, type ShoppingItem } from './api';

const ACTIVE = ['shopping', 'active'];
const CHECKED = ['shopping', 'checked'];

// The permanent shopping list (ADR-010, ADR-035). Used in the shop, on a phone, often on poor signal,
// so ticking is optimistic: the item leaves the list at once and comes back if the server refuses.
export function Shopping() {
  const queryClient = useQueryClient();
  // Refetch every 15 s while the page is visible, so two people shopping see each other's ticks.
  // TanStack Query pauses this when the tab is hidden, so an idle phone doesn't keep Neon awake.
  const active = useQuery({ queryKey: ACTIVE, queryFn: api.shopping, refetchInterval: 15_000 });
  const checked = useQuery({ queryKey: CHECKED, queryFn: () => api.recentlyChecked(10), refetchInterval: 15_000 });
  const members = useQuery({ queryKey: ['members'], queryFn: api.members });

  const [error, setError] = useState<string | null>(null);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['shopping'] });
    void queryClient.invalidateQueries({ queryKey: ['activity'] });
  };

  const tick = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'check' | 'uncheck' }) => api.tickShoppingItem(id, action),
    onMutate: async ({ id, action }) => {
      setError(null);
      await queryClient.cancelQueries({ queryKey: ['shopping'] });
      const from = action === 'check' ? ACTIVE : CHECKED;
      queryClient.setQueryData<ShoppingItem[]>(from, (items) => items?.filter((i) => i.id !== id));
    },
    onError: (e) => setError(e instanceof ApiError ? e.message : "Couldn't update the list. Check your signal and try again."),
    onSettled: refresh,
  });

  const remove = useMutation({
    mutationFn: api.removeShoppingItem,
    onMutate: async (id) => {
      setError(null);
      await queryClient.cancelQueries({ queryKey: ['shopping'] });
      queryClient.setQueryData<ShoppingItem[]>(ACTIVE, (items) => items?.filter((i) => i.id !== id));
    },
    onError: (e) => setError(e instanceof ApiError ? e.message : "Couldn't remove that item."),
    onSettled: refresh,
  });

  return (
    <section className="shopping">
      <AddItem onError={setError} onAdded={refresh} />
      {error && <p className="bad notice">{error}</p>}

      <section className="card">
        <h2>
          Shopping list{active.data && active.data.length > 0 && <span className="muted count"> {active.data.length}</span>}
        </h2>
        {active.isPending && <p className="muted">Loading…</p>}
        {active.isError && <p className="bad">Couldn't load the list.</p>}
        {active.data?.length === 0 && <p className="muted">Nothing to buy. Add something above.</p>}
        <ul className="items">
          {active.data?.map((item) => (
            <li key={item.id}>
              <button
                className="tick"
                aria-label={`Tick off ${item.name}`}
                onClick={() => tick.mutate({ id: item.id, action: 'check' })}
              />
              <div className="item-text">
                <span className="item-name">
                  {item.quantity && <span className="qty">{item.quantity}</span>}
                  {item.name}
                </span>
                {item.note && <span className="muted item-note">{item.note}</span>}
              </div>
              <button className="link danger" onClick={() => remove.mutate(item.id)} aria-label={`Remove ${item.name}`}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      </section>

      {checked.data && checked.data.length > 0 && (
        <section className="card">
          <h2>Recently ticked</h2>
          <ul className="items done">
            {checked.data.map((item) => (
              <li key={item.id}>
                <div className="item-text">
                  <span className="item-name">
                    {item.quantity && <span className="qty">{item.quantity}</span>}
                    {item.name}
                  </span>
                  <span className="muted item-note">
                    {who(item.checkedBy, members.data ?? [])} · {when(item.checkedAt!)}
                  </span>
                </div>
                <button className="link" onClick={() => tick.mutate({ id: item.id, action: 'uncheck' })}>
                  Undo
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </section>
  );
}

function AddItem({ onError, onAdded }: { onError: (message: string | null) => void; onAdded: () => void }) {
  const [name, setName] = useState('');
  const [quantity, setQuantity] = useState('');
  const [info, setInfo] = useState<string | null>(null);

  const add = useMutation({
    mutationFn: api.addShoppingItem,
    onSuccess: (result) => {
      setInfo(result.alreadyOnList ? `${result.item.name} is already on the list.` : null);
      setName('');
      setQuantity('');
      onError(null);
    },
    onError: (e) => onError(e instanceof ApiError ? e.message : "Couldn't add that item."),
    onSettled: onAdded,
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return;
    add.mutate({ name: name.trim(), quantity: quantity.trim() || null });
  };

  return (
    <form className="card add-item" onSubmit={submit}>
      <div className="row">
        <input
          className="input grow"
          placeholder="Add an item, e.g. Milk"
          value={name}
          maxLength={100}
          onChange={(e) => {
            setName(e.target.value);
            setInfo(null);
          }}
          aria-label="Item"
        />
        <input
          className="input qty-input"
          placeholder="Qty"
          value={quantity}
          maxLength={50}
          onChange={(e) => setQuantity(e.target.value)}
          aria-label="Quantity"
        />
        <button className="button" type="submit" disabled={add.isPending || !name.trim()}>
          Add
        </button>
      </div>
      {info && <p className="muted notice">{info}</p>}
    </form>
  );
}

function who(id: string | null, members: Member[]): string {
  return members.find((m) => m.userId === id)?.displayName ?? 'Someone';
}

function when(iso: string): string {
  const date = new Date(iso);
  const today = new Date();
  return date.toDateString() === today.toDateString()
    ? date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
}
