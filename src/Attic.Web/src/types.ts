export interface MeResponse {
  id: string;
  email: string;
  username: string;
}

export interface MessageDto {
  id: number;
  channelId: string;
  senderId: string;
  senderUsername: string;
  content: string;
  replyToId: number | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface PagedResult<T> {
  items: T[];
  nextCursor: string | null;
}

export interface ApiError {
  code: string;
  message: string;
}

export interface SendMessageResponse {
  ok: boolean;
  serverId: number | null;
  createdAt: string | null;
  error: string | null;
}
