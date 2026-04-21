import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { channelsApi } from '../api/channels';
import { getOrCreateHubClient } from '../api/signalr';

export function useChannelMembers(channelId: string) {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['channel-members', channelId] as const,
    queryFn: () => channelsApi.members(channelId),
    staleTime: 10_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); };
    const off1 = hub.onChannelMemberJoined(invalidate);
    const off2 = hub.onChannelMemberLeft(invalidate);
    const off3 = hub.onChannelMemberRoleChanged(invalidate);
    return () => { off1(); off2(); off3(); };
  }, [channelId, qc]);

  return q;
}
