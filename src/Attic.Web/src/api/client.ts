import type { ApiError } from '../types';

async function handle<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  const data = body ? JSON.parse(body) : null;
  if (!response.ok) {
    const err: ApiError = data && typeof data === 'object' && 'code' in data
      ? (data as ApiError)
      : { code: `http_${response.status}`, message: response.statusText };
    throw err;
  }
  return data as T;
}

export const api = {
  async get<T>(path: string): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), { credentials: 'include' });
    return handle<T>(r);
  },
  async post<T>(path: string, body?: unknown): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), {
      method: 'POST',
      credentials: 'include',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    return handle<T>(r);
  },
};
