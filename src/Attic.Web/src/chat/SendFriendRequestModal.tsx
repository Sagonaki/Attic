import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { friendsApi } from '../api/friends';
import type { ApiError } from '../types';
import { useUserSearch } from './useUserSearch';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from '@/components/ui/dialog';

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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Send friend request</DialogTitle>
          <DialogDescription>Search for a user by username to send a friend request.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Input
            placeholder="Search by username…"
            value={query}
            onChange={e => { setQuery(e.target.value); setSelected(null); }}
          />
          {query.length >= 2 && matches && matches.length > 0 && !selected && (
            <ul className="border rounded divide-y max-h-40 overflow-y-auto">
              {matches.map(u => (
                <li key={u.id}>
                  <button onClick={() => { setSelected(u.username); setQuery(u.username); }}
                          className="w-full text-left px-3 py-1.5 hover:bg-accent hover:text-accent-foreground text-sm transition-colors">
                    {u.username}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Textarea
            rows={3}
            placeholder="Optional message (max 500 chars)"
            value={text}
            maxLength={500}
            onChange={e => setText(e.target.value)}
          />
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
