import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { channelsApi } from '../api/channels';
import { getOrCreateHubClient } from '../api/signalr';
import { useUnreadCountsSubscription } from './useUnreadCounts';

export function useChannelList() {
  const qc = useQueryClient();
  const query = useQuery({
    queryKey: ['channels', 'mine'] as const,
    queryFn: () => channelsApi.listMine(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const offJoined = hub.onChannelMemberJoined(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offLeft = hub.onChannelMemberLeft(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offDeleted = hub.onChannelDeleted(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offRemoved = hub.onRemovedFromChannel(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    return () => { offJoined(); offLeft(); offDeleted(); offRemoved(); };
  }, [qc]);

  useUnreadCountsSubscription();

  return query;
}
