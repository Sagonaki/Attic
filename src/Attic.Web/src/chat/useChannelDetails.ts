import { useQuery } from '@tanstack/react-query';
import { channelsApi } from '../api/channels';

export function useChannelDetails(channelId: string) {
  return useQuery({
    queryKey: ['channel-details', channelId] as const,
    queryFn: () => channelsApi.getDetails(channelId),
    staleTime: 10_000,
  });
}
