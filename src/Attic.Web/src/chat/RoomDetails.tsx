import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { LogOut, Trash2, UserPlus, ChevronUp, ChevronDown, Ban, MoreHorizontal } from 'lucide-react';
import { channelsApi } from '../api/channels';
import { invitationsApi } from '../api/invitations';
import { useAuth } from '../auth/useAuth';
import { useChannelDetails } from './useChannelDetails';
import { useChannelMembers } from './useChannelMembers';
import { usePresence } from './usePresence';
import type { ChannelMemberSummary } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { ScrollArea } from '@/components/ui/scroll-area';
import { UserAvatar } from '@/components/ui/avatar';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { cn } from '@/lib/utils';

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
    <aside className="w-72 border-l bg-card text-sm flex flex-col">
      <div className="p-4 border-b">
        <div className="font-semibold text-base">{details?.name}</div>
        <div className="text-xs text-muted-foreground flex items-center gap-2">
          <Badge variant="outline">{details?.kind}</Badge>
          <span>{details?.memberCount} members</span>
        </div>
        {details?.description && <p className="text-sm text-muted-foreground mt-2">{details.description}</p>}
      </div>

      {details?.kind === 'private' && canManage && (
        <div className="p-4 border-b space-y-2">
          <div className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Invite</div>
          <div className="flex gap-2">
            <Input value={inviteUsername} onChange={e => setInviteUsername(e.target.value)}
                   placeholder="Username" className="h-8" />
            <Button size="sm" onClick={() => invite.mutate()} disabled={invite.isPending || !inviteUsername.trim()}>
              <UserPlus className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>
      )}

      <div className="px-4 py-2 text-xs font-semibold text-muted-foreground uppercase tracking-wide">Members</div>
      <ScrollArea className="flex-1">
        <ul className="px-2 pb-2 space-y-1">
          {members?.map(m => (
            <MemberRow
              key={m.userId}
              m={m}
              selfId={user?.id}
              canManage={canManage}
              onToggleRole={role => toggleRole.mutate({ userId: m.userId, role })}
              onBan={() => ban.mutate(m.userId)}
            />
          ))}
        </ul>
      </ScrollArea>

      <Separator />
      <div className="p-4 space-y-2">
        {!isOwner && (
          <Button variant="ghost" className="w-full justify-start" onClick={() => leave.mutate()}>
            <LogOut className="h-4 w-4" />Leave room
          </Button>
        )}
        {isOwner && (
          <Button variant="ghost" className="w-full justify-start text-destructive hover:text-destructive"
                  onClick={() => del.mutate()}>
            <Trash2 className="h-4 w-4" />Delete room
          </Button>
        )}
      </div>
    </aside>
  );
}

function MemberRow({
  m, selfId, canManage, onToggleRole, onBan,
}: {
  m: ChannelMemberSummary;
  selfId: string | undefined;
  canManage: boolean;
  onToggleRole: (role: 'admin' | 'member') => void;
  onBan: () => void;
}) {
  const presence = usePresence(m.userId);
  const dot = presence === 'online' ? 'bg-green-500'
            : presence === 'afk' ? 'bg-yellow-500'
            : 'bg-muted-foreground/40';
  const roleVariant: 'default' | 'secondary' | 'outline' =
    m.role === 'owner' ? 'default' : m.role === 'admin' ? 'secondary' : 'outline';
  const canEdit = canManage && m.userId !== selfId && m.role !== 'owner';

  return (
    <li className="flex items-center justify-between rounded-md px-2 py-1 hover:bg-accent hover:text-accent-foreground">
      <div className="flex items-center gap-2 min-w-0">
        <div className="relative">
          <UserAvatar username={m.username} className="h-7 w-7" />
          <span className={cn('absolute -bottom-0.5 -right-0.5 h-2 w-2 rounded-full ring-2 ring-card', dot)} />
        </div>
        <span className="truncate">{m.username}</span>
        <Badge variant={roleVariant} className="text-[10px] py-0 px-1.5">{m.role}</Badge>
      </div>
      {canEdit && (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="h-6 w-6 opacity-60 hover:opacity-100">
              <MoreHorizontal className="h-3.5 w-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {m.role === 'member' ? (
              <DropdownMenuItem onClick={() => onToggleRole('admin')}>
                <ChevronUp className="h-4 w-4" />Promote to admin
              </DropdownMenuItem>
            ) : (
              <DropdownMenuItem onClick={() => onToggleRole('member')}>
                <ChevronDown className="h-4 w-4" />Demote to member
              </DropdownMenuItem>
            )}
            <DropdownMenuItem onClick={onBan} className="text-destructive focus:text-destructive">
              <Ban className="h-4 w-4" />Ban
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      )}
    </li>
  );
}
