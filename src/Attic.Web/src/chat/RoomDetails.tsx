import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import { invitationsApi } from '../api/invitations';
import { useAuth } from '../auth/useAuth';
import { useChannelDetails } from './useChannelDetails';
import { useChannelMembers } from './useChannelMembers';
import { usePresence } from './usePresence';
import type { ChannelMemberSummary } from '../types';

interface MemberRowProps {
  m: ChannelMemberSummary;
  userId: string | undefined;
  canManage: boolean;
  toggleRole: { mutate: (args: { userId: string; role: 'admin' | 'member' }) => void };
  ban: { mutate: (userId: string) => void };
}

function MemberRow({ m, userId, canManage, toggleRole, ban }: MemberRowProps) {
  const presence = usePresence(m.userId);
  const dotColor =
    presence === 'online' ? 'bg-green-500' :
    presence === 'afk' ? 'bg-yellow-500' : 'bg-slate-300';
  return (
    <li className="flex items-center justify-between">
      <span className="flex items-center gap-2">
        <span className={`inline-block w-2 h-2 rounded-full ${dotColor}`} />
        {m.username}
        <span className="ml-1 text-xs text-slate-400">{m.role}</span>
      </span>
      {canManage && m.userId !== userId && m.role !== 'owner' && (
        <div className="flex gap-1">
          <button onClick={() => toggleRole.mutate({ userId: m.userId, role: m.role === 'admin' ? 'member' : 'admin' })}
                  className="text-xs text-blue-600">
            {m.role === 'admin' ? 'Demote' : 'Promote'}
          </button>
          <button onClick={() => ban.mutate(m.userId)} className="text-xs text-red-600">Ban</button>
        </div>
      )}
    </li>
  );
}

export function RoomDetails({ channelId }: { channelId: string }) {
  const { user } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const { data: details } = useChannelDetails(channelId);
  const { data: members } = useChannelMembers(channelId);

  const selfRole = members?.find(m => m.userId === user?.id)?.role;
  const canManage = selfRole === 'owner' || selfRole === 'admin';
  const isOwner = selfRole === 'owner';

  const leave = useMutation({
    mutationFn: () => channelsApi.leave(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const del = useMutation({
    mutationFn: () => channelsApi.delete(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const ban = useMutation({
    mutationFn: (userId: string) => channelsApi.banMember(channelId, userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });
  const toggleRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: 'admin' | 'member' }) =>
      channelsApi.changeRole(channelId, userId, role),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });

  const [inviteUsername, setInviteUsername] = useState('');
  const invite = useMutation({
    mutationFn: () => invitationsApi.issue(channelId, { username: inviteUsername.trim() }),
    onSuccess: () => setInviteUsername(''),
  });

  return (
    <aside className="w-72 border-l bg-white p-4 overflow-y-auto text-sm">
      <div className="mb-4">
        <div className="font-semibold text-base">{details?.name}</div>
        <div className="text-slate-500 text-xs">
          {details?.kind} · {details?.memberCount} members
        </div>
        {details?.description && <p className="text-slate-600 mt-2 text-xs">{details.description}</p>}
      </div>

      {details?.kind === 'private' && canManage && (
        <div className="mb-4 space-y-2">
          <div className="text-xs font-semibold text-slate-500 uppercase">Invite</div>
          <div className="flex gap-2">
            <input className="flex-1 border rounded px-2 py-1" placeholder="Username"
                   value={inviteUsername} onChange={e => setInviteUsername(e.target.value)} />
            <button onClick={() => invite.mutate()} disabled={invite.isPending || !inviteUsername.trim()}
                    className="px-2 py-1 text-xs bg-blue-600 text-white rounded disabled:opacity-50">
              Invite
            </button>
          </div>
        </div>
      )}

      <div className="mb-4">
        <div className="text-xs font-semibold text-slate-500 uppercase mb-1">Members</div>
        <ul className="space-y-1">
          {members?.map(m => (
            <MemberRow
              key={m.userId}
              m={m}
              userId={user?.id}
              canManage={canManage}
              toggleRole={toggleRole}
              ban={ban}
            />
          ))}
        </ul>
      </div>

      <div className="border-t pt-3 space-y-2">
        {!isOwner && <button onClick={() => leave.mutate()} className="text-xs text-slate-600">Leave room</button>}
        {isOwner && <button onClick={() => del.mutate()} className="text-xs text-red-600">Delete room</button>}
      </div>
    </aside>
  );
}
