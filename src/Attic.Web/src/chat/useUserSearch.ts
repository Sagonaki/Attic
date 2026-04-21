import { useQuery } from '@tanstack/react-query';
import { usersApi } from '../api/users';

export function useUserSearch(query: string) {
  return useQuery({
    queryKey: ['user-search', query] as const,
    queryFn: () => usersApi.search(query),
    enabled: query.length >= 2,
    staleTime: 10_000,
  });
}
