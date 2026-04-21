import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import { usePublicCatalog } from './usePublicCatalog';

export function PublicCatalog() {
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = usePublicCatalog(search);
  const items = (data?.pages ?? []).flatMap(p => p.items);

  const join = useMutation({
    mutationFn: (id: string) => channelsApi.join(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${id}`);
    },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Public rooms</h1>
      <input className="w-full border rounded px-3 py-2 mb-4" placeholder="Search by name prefix…"
             value={search} onChange={e => setSearch(e.target.value)} />
      <ul className="divide-y bg-white rounded border">
        {items.map(c => (
          <li key={c.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{c.name}</div>
              <div className="text-sm text-slate-500">{c.description ?? '—'} · {c.memberCount} members</div>
            </div>
            <button onClick={() => join.mutate(c.id)}
                    disabled={join.isPending}
                    className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
              Join
            </button>
          </li>
        ))}
        {items.length === 0 && <li className="p-6 text-center text-slate-400">No rooms yet — create one.</li>}
      </ul>
      {hasNextPage && (
        <button onClick={() => fetchNextPage()} disabled={isFetchingNextPage}
                className="mt-4 text-sm text-blue-600 self-center">
          {isFetchingNextPage ? 'Loading…' : 'Load more'}
        </button>
      )}
    </div>
  );
}
