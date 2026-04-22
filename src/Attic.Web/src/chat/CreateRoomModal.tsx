import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import type { ApiError } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from '@/components/ui/dialog';

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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New room</DialogTitle>
          <DialogDescription>Create a public or private channel.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Input placeholder="Name (3-120 chars)" value={name}
                 onChange={e => setName(e.target.value)} maxLength={120} />
          <Textarea placeholder="Description (optional)" value={description}
                    onChange={e => setDescription(e.target.value)} maxLength={1024} rows={2} />
          <div className="flex gap-4 text-sm">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="radio" checked={kind === 'public'} onChange={() => setKind('public')} /> Public
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="radio" checked={kind === 'private'} onChange={() => setKind('private')} /> Private
            </label>
          </div>
          {error && <div className="text-sm text-destructive">{error}</div>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button onClick={() => mutation.mutate()} disabled={mutation.isPending || name.trim().length < 3}>
            {mutation.isPending ? 'Creating…' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
