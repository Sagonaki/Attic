import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { friendsApi } from '../api/friends';
import { getOrCreateHubClient } from '../api/signalr';

export function useFriends() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['friends'] as const,
    queryFn: () => friendsApi.listFriends(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['friends'] }); };
    const off1 = hub.onFriendRequestDecided(invalidate);
    const off2 = hub.onFriendRemoved(invalidate);
    const off3 = hub.onBlocked(invalidate);
    return () => { off1(); off2(); off3(); };
  }, [qc]);

  return q;
}
