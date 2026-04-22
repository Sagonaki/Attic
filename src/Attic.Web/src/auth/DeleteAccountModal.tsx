import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { authExtrasApi } from '../api/authExtras';
import { disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from '@/components/ui/dialog';

export function DeleteAccountModal({ onClose }: { onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const { setUser } = useAuth();
  const navigate = useNavigate();

  const del = useMutation({
    mutationFn: () => authExtrasApi.deleteAccount({ password }),
    onSuccess: () => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    },
    onError: () => setError('Password verification failed.'),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="text-destructive">Delete account</DialogTitle>
          <DialogDescription>
            This permanently deletes your account and cascades to rooms you own. This action cannot be undone.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Input type="password" autoComplete="current-password"
                 placeholder="Confirm password"
                 value={password} onChange={e => setPassword(e.target.value)} />
          {error && <div className="text-sm text-destructive">{error}</div>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="destructive" onClick={() => del.mutate()} disabled={!password || del.isPending}>
            {del.isPending ? 'Deleting…' : 'Delete account'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
