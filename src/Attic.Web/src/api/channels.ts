import { api } from './client';
import type {
  ChannelSummary, ChannelDetails, CreateChannelRequest, UpdateChannelRequest,
  ChannelMemberSummary, BannedMemberSummary, ChangeRoleRequest, PublicCatalogItem, PagedResult,
} from '../types';

export const channelsApi = {
  listMine: () => api.get<ChannelSummary[]>('/api/channels/mine'),
  getPublic: (search?: string, cursor?: string | null) => {
    const qs = new URLSearchParams();
    if (search) qs.set('search', search);
    if (cursor) qs.set('cursor', cursor);
    return api.get<PagedResult<PublicCatalogItem>>(`/api/channels/public?${qs}`);
  },
  getDetails: (id: string) => api.get<ChannelDetails>(`/api/channels/${id}`),
  create: (req: CreateChannelRequest) => api.post<ChannelDetails>('/api/channels', req),
  update: (id: string, req: UpdateChannelRequest) => api.patch<ChannelDetails>(`/api/channels/${id}`, req),
  delete: (id: string) => api.delete<void>(`/api/channels/${id}`),
  join: (id: string) => api.post<void>(`/api/channels/${id}/join`),
  leave: (id: string) => api.post<void>(`/api/channels/${id}/leave`),
  members: (id: string) => api.get<ChannelMemberSummary[]>(`/api/channels/${id}/members`),
  bans: (id: string) => api.get<BannedMemberSummary[]>(`/api/channels/${id}/bans`),
  banMember: (id: string, userId: string) => api.delete<void>(`/api/channels/${id}/members/${userId}`),
  changeRole: (id: string, userId: string, role: 'admin' | 'member') =>
    api.post<void>(`/api/channels/${id}/members/${userId}/role`, { role } as ChangeRoleRequest),
  unban: (id: string, userId: string) => api.delete<void>(`/api/channels/${id}/bans/${userId}`),
};
