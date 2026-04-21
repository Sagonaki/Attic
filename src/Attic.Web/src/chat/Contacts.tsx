import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { friendsApi } from '../api/friends';
import { usersApi } from '../api/users';
import { useAuth } from '../auth/useAuth';
import { useFriends } from './useFriends';
import { useFriendRequests } from './useFriendRequests';
import { useOpenPersonalChat } from './useOpenPersonalChat';
import { SendFriendRequestModal } from './SendFriendRequestModal';

export function Contacts() {
  const { user } = useAuth();
  const qc = useQueryClient();
  const openChat = useOpenPersonalChat();
  const [modalOpen, setModalOpen] = useState(false);

  const { data: friends } = useFriends();
  const { data: requests } = useFriendRequests();
  const incoming = (requests ?? []).filter(r => r.recipientId === user?.id);
  const outgoing = (requests ?? []).filter(r => r.senderId === user?.id);

  const accept = useMutation({
    mutationFn: (id: string) => friendsApi.accept(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friend-requests'] });
      void qc.invalidateQueries({ queryKey: ['friends'] });
    },
  });
  const decline = useMutation({
    mutationFn: (id: string) => friendsApi.decline(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friend-requests'] }); },
  });
  const remove = useMutation({
    mutationFn: (userId: string) => friendsApi.removeFriend(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });
  const block = useMutation({
    mutationFn: (userId: string) => usersApi.block(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Contacts</h1>
        <button onClick={() => setModalOpen(true)}
                className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
          + Send friend request
        </button>
      </div>

      {incoming.length > 0 && (
        <section className="mb-6">
          <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Incoming</h2>
          <ul className="divide-y bg-white rounded border">
            {incoming.map(r => (
              <li key={r.id} className="flex items-center justify-between px-4 py-2">
                <div>
                  <div className="font-medium">{r.senderUsername}</div>
                  {r.text && <div className="text-sm text-slate-500">{r.text}</div>}
                </div>
                <div className="flex gap-2">
                  <button onClick={() => accept.mutate(r.id)}
                          className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                    Accept
                  </button>
                  <button onClick={() => decline.mutate(r.id)} className="px-3 py-1 text-sm">
                    Decline
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {outgoing.length > 0 && (
        <section className="mb-6">
          <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Outgoing</h2>
          <ul className="divide-y bg-white rounded border">
            {outgoing.map(r => (
              <li key={r.id} className="px-4 py-2">
                <div className="font-medium">{r.recipientUsername}</div>
                <div className="text-xs text-slate-400">Pending since {new Date(r.createdAt).toLocaleString()}</div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section>
        <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Friends</h2>
        {(friends ?? []).length === 0 && (
          <div className="text-slate-400 bg-white border rounded p-6 text-center">
            No friends yet — send a request to get started.
          </div>
        )}
        <ul className="divide-y bg-white rounded border">
          {(friends ?? []).map(f => (
            <li key={f.userId} className="flex items-center justify-between px-4 py-2">
              <div>
                <div className="font-medium">{f.username}</div>
                <div className="text-xs text-slate-500">Friends since {new Date(f.friendsSince).toLocaleDateString()}</div>
              </div>
              <div className="flex gap-2">
                <button onClick={() => openChat(f.username)}
                        className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                  Chat
                </button>
                <button onClick={() => remove.mutate(f.userId)} className="px-3 py-1 text-sm">
                  Remove
                </button>
                <button onClick={() => block.mutate(f.userId)} className="px-3 py-1 text-sm text-red-600">
                  Block
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      {modalOpen && <SendFriendRequestModal onClose={() => setModalOpen(false)} />}
    </div>
  );
}
