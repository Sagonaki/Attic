import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import { api } from '../api/client';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

const PAGE_SIZE = 50;

export function useChannelMessages(channelId: string) {
  const qc = useQueryClient();
  const queryKey = useMemo(() => ['channel-messages', channelId] as const, [channelId]);

  const query = useInfiniteQuery({
    queryKey,
    initialPageParam: null as string | null,
    queryFn: async ({ pageParam }) => {
      const q = pageParam ? `?before=${encodeURIComponent(pageParam)}&limit=${PAGE_SIZE}` : `?limit=${PAGE_SIZE}`;
      return api.get<PagedResult<MessageDto>>(`/api/channels/${channelId}/messages${q}`);
    },
    getNextPageParam: last => last.nextCursor,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    let active = true;
    void hub.subscribeToChannel(channelId);

    const offCreated = hub.onMessageCreated(msg => {
      if (!active || msg.channelId !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev || prev.pages.length === 0) {
          return { pages: [{ items: [msg], nextCursor: null }], pageParams: [null] };
        }
        const first = prev.pages[0];
        if (first.items.some(m => m.id === msg.id)) return prev;
        return { ...prev, pages: [{ ...first, items: [msg, ...first.items] }, ...prev.pages.slice(1)] };
      });
    });

    const offDeleted = hub.onMessageDeleted((cid, messageId) => {
      if (!active || cid !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        return {
          ...prev,
          pages: prev.pages.map(p => ({ ...p, items: p.items.filter(m => m.id !== messageId) })),
        };
      });
    });

    const offEdited = hub.onMessageEdited((cid, messageId, newContent, updatedAt) => {
      if (!active || cid !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        return {
          ...prev,
          pages: prev.pages.map(p => ({
            ...p,
            items: p.items.map(m => m.id === messageId ? { ...m, content: newContent, updatedAt } : m),
          })),
        };
      });
    });

    return () => {
      active = false;
      offCreated();
      offDeleted();
      offEdited();
      void hub.unsubscribeFromChannel(channelId);
    };
  }, [channelId, qc, queryKey]);

  const items = (query.data?.pages ?? []).flatMap(p => p.items);
  return { ...query, items };
}
