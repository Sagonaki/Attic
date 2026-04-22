import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MessageCircle, UserMinus, Ban, UserPlus, Check, X, MoreHorizontal } from 'lucide-react';
import { friendsApi } from '../api/friends';
import { usersApi } from '../api/users';
import { useAuth } from '../auth/useAuth';
import { useFriends } from './useFriends';
import { useFriendRequests } from './useFriendRequests';
import { useBlocks } from './useBlocks';
import { useOpenPersonalChat } from './useOpenPersonalChat';
import { SendFriendRequestModal } from './SendFriendRequestModal';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { UserAvatar } from '@/components/ui/avatar';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';

export function Contacts() {
  const { user } = useAuth();
  const qc = useQueryClient();
  const openChat = useOpenPersonalChat();
  const [modalOpen, setModalOpen] = useState(false);

  const { data: friends } = useFriends();
  const { data: requests } = useFriendRequests();
  const { data: blocks } = useBlocks();
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
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friends'] });
      void qc.invalidateQueries({ queryKey: ['blocks'] });
    },
  });
  const unblock = useMutation({
    mutationFn: (userId: string) => usersApi.unblock(userId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['blocks'] });
      void qc.invalidateQueries({ queryKey: ['friends'] });
    },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Contacts</h1>
        <Button onClick={() => setModalOpen(true)}>
          <UserPlus className="h-4 w-4" />Send friend request
        </Button>
      </div>

      <Tabs defaultValue="friends">
        <TabsList>
          <TabsTrigger value="friends">
            Friends <Badge variant="secondary" className="ml-2 h-5">{friends?.length ?? 0}</Badge>
          </TabsTrigger>
          <TabsTrigger value="incoming">
            Incoming <Badge variant="secondary" className="ml-2 h-5">{incoming.length}</Badge>
          </TabsTrigger>
          <TabsTrigger value="outgoing">
            Outgoing <Badge variant="secondary" className="ml-2 h-5">{outgoing.length}</Badge>
          </TabsTrigger>
          <TabsTrigger value="blocked">
            Blocked <Badge variant="secondary" className="ml-2 h-5">{(blocks ?? []).length}</Badge>
          </TabsTrigger>
        </TabsList>

        <TabsContent value="friends">
          {(friends ?? []).length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No friends yet — send a request to get started.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {(friends ?? []).map(f => (
                <li key={f.userId} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <UserAvatar username={f.username} />
                    <div>
                      <div className="font-medium">{f.username}</div>
                      <div className="text-xs text-muted-foreground">
                        Friends since {new Date(f.friendsSince).toLocaleDateString()}
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button variant="outline" size="sm" onClick={() => openChat(f.username)}>
                      <MessageCircle className="h-3.5 w-3.5" />Chat
                    </Button>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon"><MoreHorizontal className="h-4 w-4" /></Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => remove.mutate(f.userId)}>
                          <UserMinus className="h-4 w-4" />Remove friend
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem onClick={() => block.mutate(f.userId)} className="text-destructive focus:text-destructive">
                          <Ban className="h-4 w-4" />Block user
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="incoming">
          {incoming.length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No incoming requests.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {incoming.map(r => (
                <li key={r.id} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <UserAvatar username={r.senderUsername} />
                    <div>
                      <div className="font-medium">{r.senderUsername}</div>
                      {r.text && <div className="text-sm text-muted-foreground">{r.text}</div>}
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button size="sm" onClick={() => accept.mutate(r.id)}>
                      <Check className="h-3.5 w-3.5" />Accept
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => decline.mutate(r.id)}>
                      <X className="h-3.5 w-3.5" />Decline
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="outgoing">
          {outgoing.length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No outgoing requests.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {outgoing.map(r => (
                <li key={r.id} className="flex items-center gap-3 px-4 py-3">
                  <UserAvatar username={r.recipientUsername} />
                  <div>
                    <div className="font-medium">{r.recipientUsername}</div>
                    <div className="text-xs text-muted-foreground">
                      Pending since {new Date(r.createdAt).toLocaleString()}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="blocked">
          {(blocks ?? []).length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No blocked users.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {(blocks ?? []).map(b => (
                <li key={b.userId} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <UserAvatar username={b.username} />
                    <div>
                      <div className="font-medium">{b.username}</div>
                      <div className="text-xs text-muted-foreground">
                        Blocked {new Date(b.blockedAt).toLocaleDateString()}
                      </div>
                    </div>
                  </div>
                  <Button variant="outline" size="sm" onClick={() => unblock.mutate(b.userId)}>
                    Unblock
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>
      </Tabs>

      {modalOpen && <SendFriendRequestModal onClose={() => setModalOpen(false)} />}
    </div>
  );
}
