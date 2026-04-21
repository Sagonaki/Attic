import { useEffect } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

/** Mark the latest message in a channel as read. Debounced so quick scrolling doesn't hammer the hub. */
export function useMarkRead(channelId: string, latestMessageId: number | undefined) {
  useEffect(() => {
    if (!latestMessageId || latestMessageId < 0) return;
    const timer = window.setTimeout(() => {
      void getOrCreateHubClient().markRead(channelId, latestMessageId);
    }, 500);
    return () => window.clearTimeout(timer);
  }, [channelId, latestMessageId]);
}
