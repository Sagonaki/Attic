import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { disposeHubClient, getOrCreateHubClient } from '../api/signalr';
import { useAuth } from './useAuth';

/**
 * Installs a hub-wide subscription to server-initiated `ForceLogout`
 * broadcasts. Must be mounted anywhere a logged-in user's SPA is live —
 * without it, revoking a session from another tab leaves this tab stuck
 * on its last-rendered URL with a dead cookie until the next failed API call.
 */
export function useForceLogoutSubscription() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onForceLogout(() => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    });
    return () => { off(); };
  }, [navigate, setUser]);
}
