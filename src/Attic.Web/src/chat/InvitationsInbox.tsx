import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { invitationsApi } from '../api/invitations';
import { useInvitations } from './useInvitations';

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
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Invitations</h1>
      {isLoading && <div className="text-slate-500">Loading…</div>}
      {!isLoading && (data ?? []).length === 0 && (
        <div className="text-slate-400 bg-white border rounded p-6 text-center">No pending invitations.</div>
      )}
      <ul className="divide-y bg-white rounded border">
        {(data ?? []).map(inv => (
          <li key={inv.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{inv.channelName}</div>
              <div className="text-sm text-slate-500">
                Invited by {inv.inviterUsername} · {new Date(inv.createdAt).toLocaleString()}
              </div>
            </div>
            <div className="flex gap-2">
              <button onClick={() => { accept.mutate(inv.id); navigate(`/chat/${inv.channelId}`); }}
                      className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                Accept
              </button>
              <button onClick={() => decline.mutate(inv.id)} className="px-3 py-1 text-sm">
                Decline
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
