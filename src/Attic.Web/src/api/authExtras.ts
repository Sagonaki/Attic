import { api } from './client';
import type { DeleteAccountRequest } from '../types';

export const authExtrasApi = {
  deleteAccount: (req: DeleteAccountRequest) => api.post<void>('/api/auth/delete-account', req),
};
