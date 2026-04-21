import { api } from './client';
import type { ActiveSessionDto } from '../types';

export const sessionsApi = {
  listMine: () => api.get<ActiveSessionDto[]>('/api/sessions'),
  revoke: (id: string) => api.delete<void>(`/api/sessions/${id}`),
};
