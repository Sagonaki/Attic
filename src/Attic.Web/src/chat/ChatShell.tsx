import { api } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { ChatWindow } from './ChatWindow';
import { disposeHubClient } from '../api/signalr';
import { useNavigate } from 'react-router-dom';

export function ChatShell() {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();

  async function logout() {
    try {
      await api.post<void>('/api/auth/logout');
    } catch {
      // ignore
    }
    disposeHubClient();
    setUser(null);
    navigate('/login', { replace: true });
  }

  return (
    <div className="h-screen flex flex-col">
      <header className="flex items-center justify-between px-4 py-2 border-b bg-white">
        <div className="font-semibold">Attic · #lobby</div>
        <div className="text-sm text-slate-600">
          {user?.username}
          <button onClick={logout} className="ml-4 text-blue-600">Sign out</button>
        </div>
      </header>
      <main className="flex-1 overflow-hidden">
        <ChatWindow />
      </main>
    </div>
  );
}
