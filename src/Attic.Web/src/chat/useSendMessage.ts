import { useCallback, useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

export function useSendMessage(channelId: string, currentUser: { id: string; username: string }) {
  const qc = useQueryClient();
  const queryKey = useMemo(() => ['channel-messages', channelId] as const, [channelId]);

  return useCallback(async (content: string) => {
    const clientMessageId = crypto.randomUUID();
    const optimistic: MessageDto = {
      id: -Date.now(),   // negative sentinel; replaced on ack
      channelId,
      senderId: currentUser.id,
      senderUsername: currentUser.username,
      content,
      replyToId: null,
      createdAt: new Date().toISOString(),
      updatedAt: null,
      attachments: null,
    };

    qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
      if (!prev) return { pages: [{ items: [optimistic], nextCursor: null }], pageParams: [null] };
      const first = prev.pages[0] ?? { items: [], nextCursor: null };
      return { ...prev, pages: [{ ...first, items: [optimistic, ...first.items] }, ...prev.pages.slice(1)] };
    });

    try {
      const hub = getOrCreateHubClient();
      const ack = await hub.sendMessage(channelId, clientMessageId, content);
      if (!ack.ok) throw new Error(ack.error ?? 'send_failed');

      // Replace the optimistic row with the real id; the broadcast may arrive first and also append — dedupe on id.
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        const first = prev.pages[0];
        if (!first) return prev;
        const withoutOptimistic = first.items.filter(m => m.id !== optimistic.id);
        const alreadyHasReal = withoutOptimistic.some(m => m.id === ack.serverId);
        const items = alreadyHasReal
          ? withoutOptimistic
          : [
              { ...optimistic, id: ack.serverId!, createdAt: ack.createdAt! },
              ...withoutOptimistic,
            ];
        return { ...prev, pages: [{ ...first, items }, ...prev.pages.slice(1)] };
      });
    } catch (err) {
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        const first = prev.pages[0];
        if (!first) return prev;
        return { ...prev, pages: [{ ...first, items: first.items.filter(m => m.id !== optimistic.id) }, ...prev.pages.slice(1)] };
      });
      throw err;
    }
  }, [channelId, currentUser.id, currentUser.username, qc, queryKey]);
}
