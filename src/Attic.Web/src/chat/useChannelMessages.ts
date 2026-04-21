import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { api } from '../api/client';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

const PAGE_SIZE = 50;

export function useChannelMessages(channelId: string) {
  const qc = useQueryClient();
  const queryKey = ['channel-messages', channelId] as const;

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
    const off = hub.onMessageCreated(msg => {
      if (!active || msg.channelId !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev || prev.pages.length === 0) {
          return { pages: [{ items: [msg], nextCursor: null }], pageParams: [null] };
        }
        const first = prev.pages[0];
        if (first.items.some(m => m.id === msg.id)) return prev;   // already appended via ack
        return {
          ...prev,
          pages: [{ ...first, items: [msg, ...first.items] }, ...prev.pages.slice(1)],
        };
      });
    });
    return () => {
      active = false;
      off();
      void hub.unsubscribeFromChannel(channelId);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [channelId, qc]);

  const items = (query.data?.pages ?? []).flatMap(p => p.items);
  return { ...query, items };
}
