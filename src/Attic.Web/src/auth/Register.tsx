import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import type { MeResponse, ApiError } from '../types';

export function Register() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
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
    <div className="min-h-screen flex items-center justify-center bg-slate-50">
      <form onSubmit={submit} className="w-96 bg-white rounded-xl shadow p-6 space-y-4">
        <h1 className="text-xl font-semibold">Create an Attic account</h1>
        <input className="w-full border rounded px-3 py-2" type="email" placeholder="Email"
               value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email" />
        <input className="w-full border rounded px-3 py-2" placeholder="Username (3-32 chars, letters/digits/_/-)"
               value={username} onChange={e => setUsername(e.target.value)} required pattern="[A-Za-z0-9_-]{3,32}" />
        <input className="w-full border rounded px-3 py-2" type="password" placeholder="Password (min 8 chars)"
               value={password} onChange={e => setPassword(e.target.value)} required minLength={8} autoComplete="new-password" />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <button disabled={busy} className="w-full bg-blue-600 text-white rounded py-2 disabled:opacity-50">
          {busy ? 'Registering…' : 'Register'}
        </button>
        <div className="text-sm text-slate-500">
          Already have an account? <Link to="/login" className="text-blue-600">Sign in</Link>
        </div>
      </form>
    </div>
  );
}
