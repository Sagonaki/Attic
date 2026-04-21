import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { friendsApi } from '../api/friends';
import type { ApiError } from '../types';
import { useUserSearch } from './useUserSearch';

export function SendFriendRequestModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const { data: matches } = useUserSearch(query);

  const send = useMutation({
    mutationFn: () => friendsApi.send({ username: selected!, text: text.trim() || null }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friend-requests'] });
      onClose();
    },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Send failed'),
  });

  return (
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold">Send friend request</h2>
        <input className="w-full border rounded px-3 py-2"
               placeholder="Search by username…"
               value={query}
               onChange={e => { setQuery(e.target.value); setSelected(null); }} />
        {query.length >= 2 && matches && matches.length > 0 && !selected && (
          <ul className="border rounded divide-y max-h-40 overflow-y-auto">
            {matches.map(u => (
              <li key={u.id}>
                <button onClick={() => { setSelected(u.username); setQuery(u.username); }}
                        className="w-full text-left px-3 py-1 hover:bg-slate-50 text-sm">
                  {u.username}
                </button>
              </li>
            ))}
          </ul>
        )}
        <textarea className="w-full border rounded px-3 py-2 text-sm" rows={3}
                  placeholder="Optional message (max 500 chars)"
                  value={text} maxLength={500}
                  onChange={e => setText(e.target.value)} />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => send.mutate()} disabled={!selected || send.isPending}
                  className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
            {send.isPending ? 'Sending…' : 'Send'}
          </button>
        </div>
      </div>
    </div>
  );
}
