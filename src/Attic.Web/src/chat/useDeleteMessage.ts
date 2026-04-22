import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

export function useDeleteMessage(channelId: string) {
  const qc = useQueryClient();
  return useCallback(async (messageId: number) => {
    try {
      const hub = getOrCreateHubClient();
      const ack = await hub.deleteMessage(messageId);
      if (!ack.ok) throw new Error(ack.error ?? 'delete_failed');
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(
        ['channel-messages', channelId],
        prev => {
          if (!prev) return prev;
          return {
            ...prev,
            pages: prev.pages.map(p => ({ ...p, items: p.items.filter(m => m.id !== messageId) })),
          };
        }
      );
    } catch (e) {
      toast.error('Could not delete message', { description: (e as Error).message });
      throw e;
    }
  }, [channelId, qc]);
}
