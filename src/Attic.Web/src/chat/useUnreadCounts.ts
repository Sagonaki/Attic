import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { ChannelSummary } from '../types';

export function useUnreadCountsSubscription() {
  const qc = useQueryClient();
  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onUnreadChanged((channelId, count) => {
      qc.setQueryData<ChannelSummary[]>(['channels', 'mine'], prev =>
        prev?.map(c => c.id === channelId ? { ...c, unreadCount: count } : c) ?? prev);
    });
    return () => { off(); };
  }, [qc]);
}
