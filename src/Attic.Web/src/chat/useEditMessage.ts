import { useCallback } from 'react';
import { toast } from 'sonner';
import { getOrCreateHubClient } from '../api/signalr';

export function useEditMessage() {
  return useCallback(async (messageId: number, content: string) => {
    try {
      const hub = getOrCreateHubClient();
      const ack = await hub.editMessage({ messageId, content });
      if (!ack.ok) throw new Error(ack.error ?? 'edit_failed');
    } catch (e) {
      toast.error('Could not edit message', { description: (e as Error).message });
      throw e;
    }
  }, []);
}
