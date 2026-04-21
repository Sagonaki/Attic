import { useEffect } from 'react';
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { sessionsApi } from '../api/sessions';
import { getOrCreateHubClient, disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';

export function Sessions() {
  const qc = useQueryClient();
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({
    queryKey: ['sessions'] as const,
    queryFn: () => sessionsApi.listMine(),
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onForceLogout(() => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    });
    return () => { off(); };
  }, [navigate, setUser]);

  const revoke = useMutation({
    mutationFn: (id: string) => sessionsApi.revoke(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['sessions'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Active sessions</h1>
      {isLoading && <div className="text-slate-500">Loading…</div>}
      <ul className="divide-y bg-white rounded border">
        {(data ?? []).map(s => (
          <li key={s.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{s.userAgent || 'Unknown client'}</div>
              <div className="text-xs text-slate-500">
                {s.ip ?? '?'} · last seen {new Date(s.lastSeenAt).toLocaleString()}
                {s.isCurrent && <span className="ml-2 text-blue-600">(this tab)</span>}
              </div>
            </div>
            {!s.isCurrent && (
              <button onClick={() => revoke.mutate(s.id)}
                      className="px-3 py-1 text-sm text-red-600">
                Revoke
              </button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
