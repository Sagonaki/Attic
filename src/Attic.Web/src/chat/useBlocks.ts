import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { usersApi } from '../api/users';
import { getOrCreateHubClient } from '../api/signalr';

export function useBlocks() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['blocks'] as const,
    queryFn: () => usersApi.listBlocks(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['blocks'] }); };
    const off = hub.onBlocked(invalidate);
    return () => { off(); };
  }, [qc]);

  return q;
}
