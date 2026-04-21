import { useCallback } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

export function useEditMessage() {
  return useCallback(async (messageId: number, content: string) => {
    const hub = getOrCreateHubClient();
    const ack = await hub.editMessage({ messageId, content });
    if (!ack.ok) throw new Error(ack.error ?? 'edit_failed');
  }, []);
}
