import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { useDeleteMessage } from './useDeleteMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';

export function ChatWindow() {
  const { channelId } = useParams<{ channelId: string }>();
  const { user } = useAuth();

  if (!channelId) {
    return <div className="p-8 text-slate-500">Select a channel on the left to start chatting.</div>;
  }

  return <ChatWindowFor channelId={channelId} user={{ id: user!.id, username: user!.username }} />;
}

function ChatWindowFor({ channelId, user }: { channelId: string; user: { id: string; username: string } }) {
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(channelId);
  const send = useSendMessage(channelId, user);
  const del = useDeleteMessage(channelId);
  const [menuMsgId, setMenuMsgId] = useState<number | null>(null);

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
                <button
                  onClick={() => setMenuMsgId(menuMsgId === m.id ? null : m.id)}
                  className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-slate-600 px-1"
                  aria-label="Message actions"
                >
                  ⋯
                </button>
              )}
            </div>
            <div className="whitespace-pre-wrap break-words">{m.content}</div>
            {menuMsgId === m.id && (
              <div className="absolute right-2 top-8 bg-white border rounded shadow z-10">
                <button
                  className="block w-full text-left px-3 py-1 text-sm hover:bg-slate-100 text-red-600"
                  onClick={() => { void del(m.id); setMenuMsgId(null); }}
                >
                  Delete
                </button>
              </div>
            )}
          </div>
        ))}
      </div>
      <ChatInput onSend={send} />
    </div>
  );
}
