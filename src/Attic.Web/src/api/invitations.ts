import { api } from './client';
import type { InvitationDto, InviteToChannelRequest } from '../types';

export const invitationsApi = {
  listMine: () => api.get<InvitationDto[]>('/api/invitations'),
  issue: (channelId: string, req: InviteToChannelRequest) =>
    api.post<InvitationDto>(`/api/channels/${channelId}/invitations`, req),
  accept: (id: string) => api.post<void>(`/api/invitations/${id}/accept`),
  decline: (id: string) => api.post<void>(`/api/invitations/${id}/decline`),
};
