import { useEffect, useRef } from 'react';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';

const LOBBY_ID = '11111111-1111-1111-1111-000000000001';

export function ChatWindow() {
  const { user } = useAuth();
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(LOBBY_ID);
  const send = useSendMessage(LOBBY_ID, { id: user!.id, username: user!.username });

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

  // The API returns newest-first; reverse once for render so oldest is at the top and newest at the bottom.
  const ordered = [...items].reverse();

  return (
    <div className="flex flex-col h-full">
      <div ref={listRef} onScroll={onScroll} className="flex-1 overflow-y-auto p-4 space-y-2 bg-slate-50">
        {isFetchingNextPage && <div className="text-center text-xs text-slate-400">Loading older…</div>}
        {ordered.map(m => (
          <div key={m.id} className="bg-white rounded px-3 py-2 shadow-sm">
            <div className="text-xs text-slate-500">
              {m.senderUsername} · {new Date(m.createdAt).toLocaleTimeString()}
              {m.updatedAt && <span className="ml-2 text-slate-400">(edited)</span>}
              {m.id < 0 && <span className="ml-2 text-slate-400">sending…</span>}
            </div>
            <div className="whitespace-pre-wrap break-words">{m.content}</div>
          </div>
        ))}
      </div>
      <ChatInput onSend={send} />
    </div>
  );
}
