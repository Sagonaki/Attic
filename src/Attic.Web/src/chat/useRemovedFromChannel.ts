import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { getOrCreateHubClient } from '../api/signalr';

export function useRemovedFromChannel() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { channelId } = useParams<{ channelId: string }>();

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off1 = hub.onRemovedFromChannel((cid, _reason) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      if (cid === channelId) navigate('/', { replace: true });
    });
    const off2 = hub.onChannelDeleted((cid) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      if (cid === channelId) navigate('/', { replace: true });
    });
    return () => { off1(); off2(); };
  }, [channelId, navigate, qc]);
}
