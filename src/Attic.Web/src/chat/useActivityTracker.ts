import { useEffect, useRef } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

export function useActivityTracker() {
  const lastActiveSent = useRef<number>(0);

  useEffect(() => {
    let interval: number | null = null;
    let lastActivity = Date.now();

    function onActivity() {
      lastActivity = Date.now();
      if (Date.now() - lastActiveSent.current > 5_000) {
        lastActiveSent.current = Date.now();
        void getOrCreateHubClient().heartbeat('active');
      }
    }

    function tick() {
      const hub = getOrCreateHubClient();
      const state = Date.now() - lastActivity < 15_000 ? 'active' : 'idle';
      if (state === 'active') lastActiveSent.current = Date.now();
      void hub.heartbeat(state);
    }

    window.addEventListener('pointerdown', onActivity);
    window.addEventListener('keydown', onActivity);
    window.addEventListener('focus', onActivity);
    document.addEventListener('visibilitychange', onActivity);

    interval = window.setInterval(tick, 15_000);
    tick(); // immediate.

    return () => {
      window.removeEventListener('pointerdown', onActivity);
      window.removeEventListener('keydown', onActivity);
      window.removeEventListener('focus', onActivity);
      document.removeEventListener('visibilitychange', onActivity);
      if (interval !== null) window.clearInterval(interval);
    };
  }, []);
}
