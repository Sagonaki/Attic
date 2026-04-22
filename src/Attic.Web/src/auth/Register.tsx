import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { UserPlus } from 'lucide-react';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import type { MeResponse, ApiError } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export function Register() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const me = await api.post<MeResponse>('/api/auth/register', { email, username, password });
      setUser(me);
      navigate('/', { replace: true });
    } catch (ex) {
      const err = ex as ApiError;
      setError(err?.message ?? 'Registration failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-sm border bg-card text-card-foreground rounded-lg shadow-sm p-6 space-y-4">
        <div className="flex items-center gap-2">
          <UserPlus className="h-5 w-5" />
          <h1 className="text-xl font-semibold">Create account</h1>
        </div>
        <form onSubmit={onSubmit} className="space-y-3">
          <Input type="email" placeholder="Email" autoComplete="email" required
                 value={email} onChange={e => setEmail(e.target.value)} />
          <Input placeholder="Username (3-32 chars, letters/digits/_/-)"
                 value={username} onChange={e => setUsername(e.target.value)}
                 required pattern="[A-Za-z0-9_-]{3,32}" />
          <Input type="password" placeholder="Password (min 8 chars)" autoComplete="new-password"
                 required minLength={8}
                 value={password} onChange={e => setPassword(e.target.value)} />
          {error && <div className="text-sm text-destructive">{error}</div>}
          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? 'Registering…' : 'Register'}
          </Button>
        </form>
        <div className="text-sm text-muted-foreground text-center">
          Already have an account? <Link to="/login" className="underline underline-offset-4">Sign in</Link>
        </div>
      </div>
    </div>
  );
}
