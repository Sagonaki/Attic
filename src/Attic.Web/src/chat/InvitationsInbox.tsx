import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Check, X, Mail } from 'lucide-react';
import { invitationsApi } from '../api/invitations';
import { useInvitations } from './useInvitations';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';

export function InvitationsInbox() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { data, isLoading } = useInvitations();

  const accept = useMutation({
    mutationFn: (id: string) => invitationsApi.accept(id),
    onSuccess: (_data, _id) => {
      void qc.invalidateQueries({ queryKey: ['invitations'] });
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
    },
  });
  const decline = useMutation({
    mutationFn: (id: string) => invitationsApi.decline(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['invitations'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4">Invitations</h1>
      {isLoading && (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="p-4 border rounded-lg bg-card flex justify-between items-center">
              <div className="space-y-2">
                <Skeleton className="h-4 w-40" />
                <Skeleton className="h-3 w-56" />
              </div>
              <div className="flex gap-2">
                <Skeleton className="h-8 w-20" />
                <Skeleton className="h-8 w-20" />
              </div>
            </div>
          ))}
        </div>
      )}
      {!isLoading && (data ?? []).length === 0 && (
        <div className="flex flex-col items-center justify-center p-8 border rounded-lg bg-card text-center text-muted-foreground gap-2">
          <Mail className="h-8 w-8 opacity-40" />
          <span className="text-sm">No pending invitations.</span>
        </div>
      )}
      {!isLoading && (data ?? []).length > 0 && (
        <div className="rounded-lg border bg-card divide-y">
          {(data ?? []).map(inv => (
            <div key={inv.id} className="flex items-center justify-between px-4 py-3">
              <div className="min-w-0">
                <div className="font-medium truncate">{inv.channelName}</div>
                <div className="text-sm text-muted-foreground">
                  Invited by {inv.inviterUsername} · {new Date(inv.createdAt).toLocaleString()}
                </div>
              </div>
              <div className="flex gap-2 ml-4">
                <Button size="sm" onClick={() => { accept.mutate(inv.id); navigate(`/chat/${inv.channelId}`); }}>
                  <Check className="h-3.5 w-3.5" />Accept
                </Button>
                <Button variant="ghost" size="sm" onClick={() => decline.mutate(inv.id)}>
                  <X className="h-3.5 w-3.5" />Decline
                </Button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
