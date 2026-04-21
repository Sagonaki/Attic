import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { PresenceState } from '../types';

// Keyed by userId, values are the last-received PresenceState.
export function usePresence(userId?: string): PresenceState | 'unknown' {
  const qc = useQueryClient();

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onPresenceChanged((uid, state) => {
      qc.setQueryData(['presence', uid], state);
    });
    return () => { off(); };
  }, [qc]);

  const { data } = useQuery<PresenceState>({
    queryKey: ['presence', userId],
    queryFn: () => Promise.resolve<PresenceState>('offline'),
    enabled: !!userId,
    staleTime: Infinity,
    initialData: undefined,
  });
  return userId ? (data ?? 'unknown') : 'unknown';
}
