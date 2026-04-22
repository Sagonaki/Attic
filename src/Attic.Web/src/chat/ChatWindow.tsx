import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useChannelMessages } from './useChannelMessages';
import { useChannelDetails } from './useChannelDetails';
import { useSendMessage } from './useSendMessage';
import { useDeleteMessage } from './useDeleteMessage';
import { useEditMessage } from './useEditMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';
import { AttachmentPreview } from './AttachmentPreview';
import { MessageActionsMenu } from './MessageActionsMenu';
import { useMarkRead } from './useMarkRead';
import { UserAvatar } from '@/components/ui/avatar';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';

export function ChatWindow() {
  const { channelId } = useParams<{ channelId: string }>();
  const { user } = useAuth();
  if (!channelId) return <div className="p-8 text-muted-foreground">Select a channel.</div>;
  return <ChatWindowFor channelId={channelId} user={{ id: user!.id, username: user!.username }} />;
}

function ChatWindowFor({ channelId, user }: { channelId: string; user: { id: string; username: string } }) {
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(channelId);
  const { data: channel } = useChannelDetails(channelId);
  // Owner always acts as "admin" for the message-actions menu (covers the
  // admin-delete-any-message path in AuthorizationRules.CanDeleteMessage).
  // Proper per-member Admin role surfacing is a later enhancement.
  const isAdmin = !!channel && channel.ownerId === user.id;
  const latestMessageId = items[0]?.id && items[0].id > 0 ? items[0].id : undefined;
  useMarkRead(channelId, latestMessageId);
  const send = useSendMessage(channelId, user);
  const del = useDeleteMessage(channelId);
  const edit = useEditMessage();
  const [replyTo, setReplyTo] = useState<{ messageId: number; snippet: string } | null>(null);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState('');

  const listRef = useRef<HTMLDivElement>(null);
  const lockedToBottom = useRef(true);

  useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (lockedToBottom.current) el.scrollTop = el.scrollHeight;
  }, [items.length]);

  function onScroll(e: React.UIEvent<HTMLDivElement>) {
    const el = e.currentTarget;
    lockedToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
    if (el.scrollTop === 0 && hasNextPage && !isFetchingNextPage) void fetchNextPage();
  }

  const ordered = [...items].reverse();
  const byId = new Map(ordered.map(m => [m.id, m]));

  async function saveEdit() {
    if (editingId === null) return;
    const draft = editDraft.trim();
    if (!draft) return;
    try { await edit(editingId, draft); } catch { /* ignore — UI will invalidate */ }
    setEditingId(null);
    setEditDraft('');
  }

  return (
    <div className="flex flex-col h-full">
      <div ref={listRef} onScroll={onScroll} className="flex-1 overflow-y-auto p-4 space-y-1 bg-background">
        {isFetchingNextPage && <div className="text-center text-xs text-muted-foreground">Loading older…</div>}
        {ordered.map(m => (
          <div key={m.id} className="group flex gap-2 hover:bg-accent/40 rounded-md px-2 py-1 transition-colors">
            <UserAvatar username={m.senderUsername} className="h-8 w-8 mt-0.5" />
            <div className="flex-1 min-w-0">
              <div className="flex items-center justify-between gap-2">
                <div className="text-xs text-muted-foreground">
                  <span className="font-medium text-foreground">{m.senderUsername}</span>
                  <span className="ml-2">{new Date(m.createdAt).toLocaleTimeString()}</span>
                  {m.updatedAt && <span className="ml-2">(edited)</span>}
                  {m.id < 0 && <span className="ml-2 italic">sending…</span>}
                </div>
                {m.id > 0 && (
                  <MessageActionsMenu
                    isOwn={m.senderId === user.id}
                    isAdmin={isAdmin}
                    onEdit={() => { setEditingId(m.id); setEditDraft(m.content); }}
                    onReply={() => setReplyTo({ messageId: m.id, snippet: m.content.slice(0, 80) })}
                    onDelete={() => void del(m.id)}
                  />
                )}
              </div>
              {m.replyToId && byId.get(m.replyToId) && (
                <div className="text-xs text-muted-foreground border-l-2 border-muted-foreground/30 pl-2 my-1">
                  <span className="font-medium">{byId.get(m.replyToId)!.senderUsername}: </span>
                  {byId.get(m.replyToId)!.content.slice(0, 80)}
                </div>
              )}
              {editingId === m.id ? (
                <div className="flex gap-2 items-center">
                  <Input value={editDraft} onChange={e => setEditDraft(e.target.value)}
                         onKeyDown={e => { if (e.key === 'Enter') void saveEdit(); if (e.key === 'Escape') setEditingId(null); }}
                         autoFocus />
                  <Button size="sm" onClick={saveEdit}>Save</Button>
                  <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>Cancel</Button>
                </div>
              ) : (
                <>
                  <div className="whitespace-pre-wrap break-words text-sm">{m.content}</div>
                  {m.attachments?.map(a => <AttachmentPreview key={a.id} attachment={a} />)}
                </>
              )}
            </div>
          </div>
        ))}
      </div>
      <ChatInput onSend={send} replyTo={replyTo} onCancelReply={() => setReplyTo(null)} />
    </div>
  );
}
