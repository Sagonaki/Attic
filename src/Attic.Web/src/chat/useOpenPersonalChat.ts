import { useCallback } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { personalChatsApi } from '../api/personalChats';

export function useOpenPersonalChat() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const mutation = useMutation({
    mutationFn: (username: string) => personalChatsApi.open({ username }),
    onSuccess: (channel) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${channel.id}`);
    },
  });
  return useCallback((username: string) => mutation.mutate(username), [mutation]);
}
