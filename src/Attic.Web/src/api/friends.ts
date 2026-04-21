import { api } from './client';
import type { FriendDto, FriendRequestDto, SendFriendRequestRequest } from '../types';

export const friendsApi = {
  listFriends: () => api.get<FriendDto[]>('/api/friends'),
  removeFriend: (userId: string) => api.delete<void>(`/api/friends/${userId}`),
  listRequests: () => api.get<FriendRequestDto[]>('/api/friend-requests'),
  send: (req: SendFriendRequestRequest) => api.post<FriendRequestDto>('/api/friend-requests', req),
  accept: (id: string) => api.post<void>(`/api/friend-requests/${id}/accept`),
  decline: (id: string) => api.post<void>(`/api/friend-requests/${id}/decline`),
};
