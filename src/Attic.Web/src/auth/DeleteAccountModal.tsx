import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { authExtrasApi } from '../api/authExtras';
import { disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';

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
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold text-red-600">Delete account</h2>
        <p className="text-sm text-slate-600">
          This permanently deletes your account and cascades to rooms you own. This action cannot be undone.
        </p>
        <input type="password" autoComplete="current-password"
               className="w-full border rounded px-3 py-2" placeholder="Confirm password"
               value={password} onChange={e => setPassword(e.target.value)} />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => del.mutate()} disabled={!password || del.isPending}
                  className="px-3 py-1 text-sm bg-red-600 text-white rounded disabled:opacity-50">
            {del.isPending ? 'Deleting…' : 'Delete account'}
          </button>
        </div>
      </div>
    </div>
  );
}
