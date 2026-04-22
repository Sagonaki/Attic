import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import { Laptop, Smartphone, Monitor } from 'lucide-react';
import { sessionsApi } from '../api/sessions';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';

function deviceIcon(userAgent: string) {
  const ua = userAgent.toLowerCase();
  if (ua.includes('mobile') || ua.includes('iphone') || ua.includes('android')) return Smartphone;
  if (ua.includes('mac') || ua.includes('windows') || ua.includes('linux')) return Laptop;
  return Monitor;
}

export function Sessions() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({
    queryKey: ['sessions'] as const,
    queryFn: () => sessionsApi.listMine(),
  });

  // The ForceLogout subscription lives in ChatShell so every authenticated tab
  // reacts — not just the Sessions page — see `useForceLogoutSubscription`.

  const revoke = useMutation({
    mutationFn: (id: string) => sessionsApi.revoke(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['sessions'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4">Active sessions</h1>
      {isLoading && (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="p-4 border rounded-lg bg-card flex gap-3">
              <Skeleton className="h-6 w-6 rounded" />
              <div className="flex-1 space-y-2"><Skeleton className="h-4 w-48" /><Skeleton className="h-3 w-32" /></div>
            </div>
          ))}
        </div>
      )}
      <ul className="space-y-2">
        {(data ?? []).map(s => {
          const Icon = deviceIcon(s.userAgent);
          return (
            <li key={s.id} className="flex items-center gap-3 border rounded-lg bg-card px-4 py-3">
              <Icon className="h-5 w-5 text-muted-foreground" />
              <div className="flex-1 min-w-0">
                <div className="font-medium flex items-center gap-2">
                  <span className="truncate">{s.userAgent || 'Unknown client'}</span>
                  {s.isCurrent && <Badge variant="default">This tab</Badge>}
                </div>
                <div className="text-xs text-muted-foreground">
                  {s.ip ?? '?'} · last seen {new Date(s.lastSeenAt).toLocaleString()}
                </div>
              </div>
              {!s.isCurrent && (
                <Button variant="ghost" size="sm" onClick={() => revoke.mutate(s.id)}
                        className="text-destructive hover:text-destructive">
                  Revoke
                </Button>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
