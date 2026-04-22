export interface MeResponse {
  id: string;
  email: string;
  username: string;
}

export interface AttachmentDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  comment: string | null;
}

export interface UploadAttachmentResponse {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface EditMessageRequest {
  messageId: number;
  content: string;
}

export interface EditMessageResponse {
  ok: boolean;
  updatedAt: string | null;
  error: string | null;
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
  attachments: AttachmentDto[] | null;
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

export interface UserSummary {
  id: string;
  username: string;
}

export interface ChannelSummary {
  id: string;
  kind: 'public' | 'private' | 'personal';
  name: string | null;
  description: string | null;
  ownerId: string | null;
  memberCount: number;
  unreadCount: number;
  otherMemberUsername: string | null;
}

export interface ChannelDetails {
  id: string;
  kind: 'public' | 'private' | 'personal';
  name: string | null;
  description: string | null;
  ownerId: string | null;
  createdAt: string;
  memberCount: number;
}

export interface CreateChannelRequest {
  name: string;
  description: string | null;
  kind: 'public' | 'private';
}

export interface UpdateChannelRequest {
  name: string | null;
  description: string | null;
}

export interface ChannelMemberSummary {
  userId: string;
  username: string;
  role: 'owner' | 'admin' | 'member';
  joinedAt: string;
}

export interface BannedMemberSummary {
  userId: string;
  username: string;
  bannedById: string;
  bannedByUsername: string | null;
  bannedAt: string;
  reason: string | null;
}

export interface ChangeRoleRequest {
  role: 'admin' | 'member';
}

export interface PublicCatalogItem {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
}

export interface InvitationDto {
  id: string;
  channelId: string;
  channelName: string;
  inviterId: string;
  inviterUsername: string;
  status: string;
  createdAt: string;
  decidedAt: string | null;
}

export interface InviteToChannelRequest {
  username: string;
}

export interface FriendRequestDto {
  id: string;
  senderId: string;
  senderUsername: string;
  recipientId: string;
  recipientUsername: string;
  text: string | null;
  status: string;
  createdAt: string;
  decidedAt: string | null;
}

export interface FriendDto {
  userId: string;
  username: string;
  friendsSince: string;
}

export interface SendFriendRequestRequest {
  username: string;
  text: string | null;
}

export interface UserSearchResult {
  id: string;
  username: string;
}

export interface OpenPersonalChatRequest {
  username: string;
}

export interface SendMessageRequest {
  channelId: string;
  clientMessageId: string;
  content: string;
  replyToId: number | null;
  attachmentIds: string[] | null;
}

export type PresenceState = 'online' | 'afk' | 'offline';

export interface ActiveSessionDto {
  id: string;
  userAgent: string;
  ip: string | null;
  createdAt: string;
  lastSeenAt: string;
  isCurrent: boolean;
}

export interface DeleteAccountRequest {
  password: string;
}

export interface ForgotPasswordRequest { email: string; }
export interface ChangePasswordRequest { currentPassword: string; newPassword: string; }
export interface BlockedUserDto { userId: string; username: string; blockedAt: string; }
