import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { UserPlus } from 'lucide-react';
import { toast } from 'sonner';
import { invitationsApi } from '../api/invitations';
import { useUserSearch } from './useUserSearch';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import type { ApiError } from '../types';

export function InviteToChannelModal({ channelId, onClose }: { channelId: string; onClose: () => void }) {
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { data: matches } = useUserSearch(query);

  const send = useMutation({
    mutationFn: () => invitationsApi.issue(channelId, { username: selected! }),
    onSuccess: () => { toast.success(`Invitation sent to ${selected}.`); onClose(); },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Invitation failed'),
  });

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><UserPlus className="h-4 w-4" />Invite to room</DialogTitle>
          <DialogDescription>Search for a user by username to send an invitation.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Input placeholder="Search by username…" value={query}
                 onChange={e => { setQuery(e.target.value); setSelected(null); }} />
          {query.length >= 2 && matches && matches.length > 0 && !selected && (
            <ul className="border rounded divide-y max-h-40 overflow-y-auto bg-card">
              {matches.map(u => (
                <li key={u.id}>
                  <button onClick={() => { setSelected(u.username); setQuery(u.username); }}
                          className="w-full text-left px-3 py-1.5 hover:bg-accent hover:text-accent-foreground text-sm">
                    {u.username}
                  </button>
                </li>
              ))}
            </ul>
          )}
          {error && <div className="text-sm text-destructive">{error}</div>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button onClick={() => send.mutate()} disabled={!selected || send.isPending}>
            {send.isPending ? 'Sending…' : 'Send'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
