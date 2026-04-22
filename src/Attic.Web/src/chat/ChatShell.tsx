import { useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { LogOut, Trash2, User as UserIcon, Settings } from 'lucide-react';
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
import { useForceLogoutSubscription } from '../auth/useForceLogoutSubscription';
import { Sessions } from '../auth/Sessions';
import { MyProfile } from '../auth/MyProfile';
import { DeleteAccountModal } from '../auth/DeleteAccountModal';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
  DropdownMenuSeparator, DropdownMenuLabel,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { UserAvatar } from '@/components/ui/avatar';
import { ThemeToggle } from '../theme/ThemeToggle';
import logoUrl from '../assets/attic-logo.jpg';

export function ChatShell() {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { channelId } = useParams<{ channelId: string }>();
  useRemovedFromChannel();
  useActivityTracker();
  // Install the ForceLogout handler at the shell level so *any* open tab
  // reacts to a session revoke from elsewhere — not only the Sessions page.
  useForceLogoutSubscription();
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
      <header className="flex items-center justify-between px-4 py-2 border-b bg-card text-card-foreground">
        <div className="flex items-center gap-2 font-semibold">
          <img src={logoUrl} alt="Attic" className="h-6 w-6 rounded object-cover" />
          <span>Attic</span>
        </div>
        <div className="flex items-center gap-1">
          <ThemeToggle />
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="gap-2">
                <UserAvatar username={user?.username} className="h-6 w-6" />
                <span className="text-sm">{user?.username}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>Account</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem asChild>
                <Link to="/profile"><UserIcon className="h-4 w-4" /> My profile</Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/settings/sessions"><Settings className="h-4 w-4" /> Active sessions</Link>
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={logout}>
                <LogOut className="h-4 w-4" /> Sign out
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => setDeleteOpen(true)} className="text-destructive focus:text-destructive">
                <Trash2 className="h-4 w-4" /> Delete account
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>
      <div className="flex-1 flex overflow-hidden">
        <Sidebar onCreate={() => setCreateOpen(true)} />
        <main className="flex-1 flex overflow-hidden">
          {pathname === '/catalog' && <PublicCatalog />}
          {pathname === '/invitations' && <InvitationsInbox />}
          {pathname === '/contacts' && <Contacts />}
          {pathname === '/settings/sessions' && <Sessions />}
          {pathname === '/profile' && <MyProfile />}
          {pathname !== '/catalog' && pathname !== '/invitations' && pathname !== '/contacts' && pathname !== '/settings/sessions' && pathname !== '/profile' && (
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
