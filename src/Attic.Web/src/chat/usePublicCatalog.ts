import { useInfiniteQuery } from '@tanstack/react-query';
import { channelsApi } from '../api/channels';

export function usePublicCatalog(search: string) {
  return useInfiniteQuery({
    queryKey: ['channels', 'public', search] as const,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => channelsApi.getPublic(search || undefined, pageParam),
    getNextPageParam: (last) => last.nextCursor,
  });
}
