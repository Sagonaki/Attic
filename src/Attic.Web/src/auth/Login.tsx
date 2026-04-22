import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { LogIn } from 'lucide-react';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import type { MeResponse, ApiError } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export function Login() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.post<MeResponse>('/api/auth/login', { email, password });
      setUser(me);
      navigate('/', { replace: true });
    } catch (ex) {
      const err = ex as ApiError;
      setError(err?.message ?? 'Login failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-sm border bg-card text-card-foreground rounded-lg shadow-sm p-6 space-y-4">
        <div className="flex items-center gap-2">
          <LogIn className="h-5 w-5" />
          <h1 className="text-xl font-semibold">Sign in</h1>
        </div>
        <form onSubmit={onSubmit} className="space-y-3">
          <Input type="email" placeholder="Email" autoComplete="email" required
                 value={email} onChange={e => setEmail(e.target.value)} />
          <Input type="password" placeholder="Password" autoComplete="current-password" required
                 value={password} onChange={e => setPassword(e.target.value)} />
          {error && <div className="text-sm text-destructive">{error}</div>}
          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
        <div className="text-sm text-muted-foreground text-center">
          No account? <Link to="/register" className="underline underline-offset-4">Register</Link>
        </div>
      </div>
    </div>
  );
}
