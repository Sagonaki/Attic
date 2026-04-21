import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { useDeleteMessage } from './useDeleteMessage';
import { useEditMessage } from './useEditMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';
import { AttachmentPreview } from './AttachmentPreview';
import { MessageActionsMenu } from './MessageActionsMenu';

export function ChatWindow() {
  const { channelId } = useParams<{ channelId: string }>();
  const { user } = useAuth();
  if (!channelId) return <div className="p-8 text-slate-500">Select a channel.</div>;
  return <ChatWindowFor channelId={channelId} user={{ id: user!.id, username: user!.username }} />;
}

function ChatWindowFor({ channelId, user }: { channelId: string; user: { id: string; username: string } }) {
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(channelId);
  const send = useSendMessage(channelId, user);
  const del = useDeleteMessage(channelId);
  const edit = useEditMessage();
  const [menuMsgId, setMenuMsgId] = useState<number | null>(null);
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
      <div ref={listRef} onScroll={onScroll} className="flex-1 overflow-y-auto p-4 space-y-2 bg-slate-50">
        {isFetchingNextPage && <div className="text-center text-xs text-slate-400">Loading older…</div>}
        {ordered.map(m => (
          <div key={m.id} className="bg-white rounded px-3 py-2 shadow-sm group relative">
            <div className="text-xs text-slate-500 flex justify-between">
              <span>
                {m.senderUsername} · {new Date(m.createdAt).toLocaleTimeString()}
                {m.updatedAt && <span className="ml-2 text-slate-400">(edited)</span>}
                {m.id < 0 && <span className="ml-2 text-slate-400">sending…</span>}
              </span>
              {m.id > 0 && (
                <button onClick={() => setMenuMsgId(menuMsgId === m.id ? null : m.id)}
                        className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-slate-600 px-1"
                        aria-label="Message actions">⋯</button>
              )}
            </div>
            {m.replyToId && byId.get(m.replyToId) && (
              <div className="text-xs text-slate-500 border-l-2 border-slate-300 pl-2 mb-1">
                {byId.get(m.replyToId)!.senderUsername}: {byId.get(m.replyToId)!.content.slice(0, 80)}
              </div>
            )}
            {editingId === m.id ? (
              <div className="flex gap-2">
                <input className="flex-1 border rounded px-2 py-1 text-sm"
                       value={editDraft} onChange={e => setEditDraft(e.target.value)}
                       onKeyDown={e => { if (e.key === 'Enter') void saveEdit(); if (e.key === 'Escape') setEditingId(null); }} />
                <button onClick={saveEdit} className="text-xs text-blue-600">Save</button>
                <button onClick={() => setEditingId(null)} className="text-xs">Cancel</button>
              </div>
            ) : (
              <>
                <div className="whitespace-pre-wrap break-words">{m.content}</div>
                {m.attachments?.map(a => <AttachmentPreview key={a.id} attachment={a} />)}
              </>
            )}
            {menuMsgId === m.id && (
              <MessageActionsMenu
                isOwn={m.senderId === user.id}
                isAdmin={false}
                onEdit={() => { setEditingId(m.id); setEditDraft(m.content); setMenuMsgId(null); }}
                onReply={() => { setReplyTo({ messageId: m.id, snippet: m.content.slice(0, 80) }); setMenuMsgId(null); }}
                onDelete={() => { void del(m.id); setMenuMsgId(null); }}
                onClose={() => setMenuMsgId(null)}
              />
            )}
          </div>
        ))}
      </div>
      <ChatInput onSend={send} replyTo={replyTo} onCancelReply={() => setReplyTo(null)} />
    </div>
  );
}
