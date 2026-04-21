import { api } from './client';
import type { ChannelDetails } from '../types';
import type { OpenPersonalChatRequest } from '../types';

export const personalChatsApi = {
  open: (req: OpenPersonalChatRequest) => api.post<ChannelDetails>('/api/personal-chats/open', req),
};
