import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { ChatWindow } from './ChatWindow';
import { Sidebar } from './Sidebar';
import { CreateRoomModal } from './CreateRoomModal';
import { PublicCatalog } from './PublicCatalog';
import { RoomDetails } from './RoomDetails';
import { InvitationsInbox } from './InvitationsInbox';
import { Contacts } from './Contacts';
import { disposeHubClient } from '../api/signalr';
import { useRemovedFromChannel } from './useRemovedFromChannel';
import { useActivityTracker } from './useActivityTracker';
import { Sessions } from '../auth/Sessions';
import { DeleteAccountModal } from '../auth/DeleteAccountModal';

export function ChatShell() {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { channelId } = useParams<{ channelId: string }>();
  useRemovedFromChannel();
  useActivityTracker();
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  async function logout() {
    try { await api.post<void>('/api/auth/logout'); } catch { /* ignore */ }
    disposeHubClient();
    setUser(null);
    navigate('/login', { replace: true });
  }

  return (
    <div className="h-screen flex flex-col">
      <header className="flex items-center justify-between px-4 py-2 border-b bg-white">
        <div className="font-semibold">Attic</div>
        <div className="text-sm text-slate-600">
          {user?.username}
          <button onClick={logout} className="ml-4 text-blue-600">Sign out</button>
          <button onClick={() => setDeleteOpen(true)} className="ml-4 text-red-600">Delete account</button>
        </div>
      </header>
      <div className="flex-1 flex overflow-hidden">
        <Sidebar onCreate={() => setCreateOpen(true)} />
        <main className="flex-1 flex overflow-hidden">
          {pathname === '/catalog' && <PublicCatalog />}
          {pathname === '/invitations' && <InvitationsInbox />}
          {pathname === '/contacts' && <Contacts />}
          {pathname === '/settings/sessions' && <Sessions />}
          {pathname !== '/catalog' && pathname !== '/invitations' && pathname !== '/contacts' && pathname !== '/settings/sessions' && (
            <>
              <div className="flex-1 flex flex-col"><ChatWindow /></div>
              {channelId && <RoomDetails channelId={channelId} />}
            </>
          )}
        </main>
      </div>
      {createOpen && <CreateRoomModal onClose={() => setCreateOpen(false)} />}
      {deleteOpen && <DeleteAccountModal onClose={() => setDeleteOpen(false)} />}
    </div>
  );
}
