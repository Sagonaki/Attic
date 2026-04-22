import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Search, Users, LogIn } from 'lucide-react';
import { channelsApi } from '../api/channels';
import { usePublicCatalog } from './usePublicCatalog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';

export function PublicCatalog() {
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, isLoading } = usePublicCatalog(search);
  const items = (data?.pages ?? []).flatMap(p => p.items);

  const join = useMutation({
    mutationFn: (id: string) => channelsApi.join(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${id}`);
    },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4">Public rooms</h1>
      <div className="relative mb-4">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9" placeholder="Search by name prefix…" value={search}
               onChange={e => setSearch(e.target.value)} />
      </div>
      <div className="rounded-lg border bg-card divide-y">
        {isLoading && Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="p-4 flex justify-between">
            <div className="space-y-2">
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-3 w-64" />
            </div>
            <Skeleton className="h-8 w-16" />
          </div>
        ))}
        {!isLoading && items.length === 0 && (
          <div className="p-8 text-center text-muted-foreground text-sm">No rooms yet — create one.</div>
        )}
        {items.map(c => (
          <div key={c.id} className="flex items-center justify-between px-4 py-3">
            <div className="min-w-0">
              <div className="font-medium truncate">{c.name}</div>
              <div className="text-sm text-muted-foreground flex items-center gap-1">
                {c.description ?? '—'}
                <span className="mx-1">·</span>
                <Users className="h-3 w-3" /> {c.memberCount}
              </div>
            </div>
            <Button onClick={() => join.mutate(c.id)} disabled={join.isPending}>
              <LogIn className="h-4 w-4" />Join
            </Button>
          </div>
        ))}
      </div>
      {hasNextPage && (
        <Button variant="ghost" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}
                className="mt-4 self-center">
          {isFetchingNextPage ? 'Loading…' : 'Load more'}
        </Button>
      )}
    </div>
  );
}
