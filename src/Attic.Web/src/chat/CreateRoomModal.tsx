import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import type { ApiError } from '../types';

export function CreateRoomModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [kind, setKind] = useState<'public' | 'private'>('public');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => channelsApi.create({ name: name.trim(), description: description.trim() || null, kind }),
    onSuccess: (channel) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${channel.id}`);
      onClose();
    },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Create failed'),
  });

  return (
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold">New room</h2>
        <input className="w-full border rounded px-3 py-2" placeholder="Name (3-120 chars)"
               value={name} onChange={e => setName(e.target.value)} maxLength={120} />
        <input className="w-full border rounded px-3 py-2" placeholder="Description (optional)"
               value={description} onChange={e => setDescription(e.target.value)} maxLength={1024} />
        <div className="flex gap-4 text-sm">
          <label className="flex items-center gap-2">
            <input type="radio" checked={kind === 'public'} onChange={() => setKind('public')} /> Public
          </label>
          <label className="flex items-center gap-2">
            <input type="radio" checked={kind === 'private'} onChange={() => setKind('private')} /> Private
          </label>
        </div>
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => mutation.mutate()}
                  disabled={mutation.isPending || name.trim().length < 3}
                  className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
            {mutation.isPending ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  );
}
