import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { friendsApi } from '../api/friends';
import { getOrCreateHubClient } from '../api/signalr';

export function useFriendRequests() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['friend-requests'] as const,
    queryFn: () => friendsApi.listRequests(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['friend-requests'] }); };
    const off1 = hub.onFriendRequestReceived(invalidate);
    const off2 = hub.onFriendRequestDecided(invalidate);
    return () => { off1(); off2(); };
  }, [qc]);

  return q;
}
