import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { KeyRound, User as UserIcon } from 'lucide-react';
import { toast } from 'sonner';
import { authExtrasApi } from '../api/authExtras';
import { useAuth } from './useAuth';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { UserAvatar } from '@/components/ui/avatar';
import { Separator } from '@/components/ui/separator';

export function MyProfile() {
  const { user } = useAuth();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);

  const change = useMutation({
    mutationFn: () => authExtrasApi.changePassword({ currentPassword: current, newPassword: next }),
    onSuccess: () => {
      toast.success('Password updated.');
      setCurrent(''); setNext(''); setConfirm(''); setError(null);
    },
    onError: (e: Error) => setError(e.message ?? 'Failed to change password'),
  });

  function submit() {
    setError(null);
    if (next.length < 8) { setError('New password must be at least 8 characters.'); return; }
    if (next !== confirm) { setError("Passwords don't match."); return; }
    change.mutate();
  }

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4 flex items-center gap-2">
        <UserIcon className="h-5 w-5" />My profile
      </h1>

      <div className="max-w-xl space-y-6">
        <section className="border rounded-lg bg-card p-4 flex items-center gap-4">
          <UserAvatar username={user?.username} className="h-14 w-14" />
          <div>
            <div className="font-semibold">{user?.username}</div>
            <div className="text-sm text-muted-foreground">{user?.email}</div>
          </div>
        </section>

        <Separator />

        <section className="border rounded-lg bg-card p-4 space-y-3">
          <h2 className="font-semibold flex items-center gap-2"><KeyRound className="h-4 w-4" />Change password</h2>
          <Input type="password" placeholder="Current password" autoComplete="current-password"
                 value={current} onChange={e => setCurrent(e.target.value)} />
          <Input type="password" placeholder="New password (min 8 chars)" autoComplete="new-password"
                 value={next} onChange={e => setNext(e.target.value)} />
          <Input type="password" placeholder="Confirm new password" autoComplete="new-password"
                 value={confirm} onChange={e => setConfirm(e.target.value)} />
          {error && <div className="text-sm text-destructive">{error}</div>}
          <Button onClick={submit} disabled={!current || !next || !confirm || change.isPending}>
            {change.isPending ? 'Updating…' : 'Update password'}
          </Button>
        </section>
      </div>
    </div>
  );
}
