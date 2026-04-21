import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { invitationsApi } from '../api/invitations';
import { getOrCreateHubClient } from '../api/signalr';

export function useInvitations() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['invitations'] as const,
    queryFn: () => invitationsApi.listMine(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onInvitationReceived(() => { void qc.invalidateQueries({ queryKey: ['invitations'] }); });
    return () => { off(); };
  }, [qc]);

  return q;
}
