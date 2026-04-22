import { api } from './client';
import type { DeleteAccountRequest, ForgotPasswordRequest, ChangePasswordRequest } from '../types';

export const authExtrasApi = {
  deleteAccount: (req: DeleteAccountRequest) => api.post<void>('/api/auth/delete-account', req),
  forgotPassword: (req: ForgotPasswordRequest) => api.post<{ ok: boolean }>('/api/auth/password/forgot', req),
  changePassword: (req: ChangePasswordRequest) => api.post<void>('/api/auth/change-password', req),
};
