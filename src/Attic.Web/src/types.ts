export type AuthMode = 'signin' | 'register' | 'forgot-password';
export type View = 'chat' | 'contacts' | 'sessions' | 'profile';
export type ChatCategory = 'public' | 'private' | 'personal';

export interface Room {
  id: string;
  name: string;
  type: 'public' | 'private';
  unreadCount?: number;
  memberCount?: number;
  createdAt?: string;
}

export interface Contact {
  id: string;
  name: string;
  status: 'online' | 'offline' | 'afk';
  unreadCount?: number;
}

export interface Message {
  id: string;
  sender: string;
  time: string;
  content: string;
  type: 'text' | 'file' | 'reply';
  fileName?: string;
  fileComment?: string;
  replyToId?: string;
  replyToContent?: string;
  replyToSender?: string;
  isEdited?: boolean;
}

export interface Session {
  id: string;
  device: string;
  browser: string;
  ip: string;
  location: string;
  lastActive: string;
  isCurrent?: boolean;
}

export interface FriendRequest {
  id: string;
  username: string;
  content?: string;
  status: 'pending' | 'accepted' | 'declined';
}

export interface BannedUser {
  name: string;
  by: string;
  date: string;
}

export interface SentInvitation {
  username: string;
  status: string;
  date: string;
}
