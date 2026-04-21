import * as signalR from '@microsoft/signalr';
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto, FriendRequestDto, EditMessageRequest, EditMessageResponse, PresenceState } from '../types';

export interface HubClient {
  connection: signalR.HubConnection;
  subscribeToChannel(channelId: string): Promise<void>;
  unsubscribeFromChannel(channelId: string): Promise<void>;
  sendMessage(channelId: string, clientMessageId: string, content: string, replyToId: number | null, attachmentIds: string[] | null): Promise<SendMessageResponse>;
  deleteMessage(messageId: number): Promise<{ ok: boolean; error?: string }>;
  editMessage(req: EditMessageRequest): Promise<EditMessageResponse>;
  onMessageCreated(cb: (m: MessageDto) => void): () => void;
  onMessageDeleted(cb: (channelId: string, messageId: number) => void): () => void;
  onMessageEdited(cb: (channelId: string, messageId: number, newContent: string, updatedAt: string) => void): () => void;
  onChannelMemberJoined(cb: (channelId: string, member: ChannelMemberSummary) => void): () => void;
  onChannelMemberLeft(cb: (channelId: string, userId: string) => void): () => void;
  onChannelMemberRoleChanged(cb: (channelId: string, userId: string, role: string) => void): () => void;
  onRemovedFromChannel(cb: (channelId: string, reason: string) => void): () => void;
  onChannelDeleted(cb: (channelId: string) => void): () => void;
  onInvitationReceived(cb: (invitation: InvitationDto) => void): () => void;
  onFriendRequestReceived(cb: (dto: FriendRequestDto) => void): () => void;
  onFriendRequestDecided(cb: (requestId: string, status: string) => void): () => void;
  onFriendRemoved(cb: (otherUserId: string) => void): () => void;
  onBlocked(cb: (blockerId: string) => void): () => void;
  heartbeat(state: 'active' | 'idle'): Promise<void>;
  markRead(channelId: string, lastMessageId: number): Promise<void>;
  onPresenceChanged(cb: (userId: string, state: PresenceState) => void): () => void;
  onUnreadChanged(cb: (channelId: string, count: number) => void): () => void;
  onForceLogout(cb: (sessionId: string) => void): () => void;
}

let singleton: HubClient | null = null;

export function getOrCreateHubClient(): HubClient {
  if (singleton) return singleton;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub', { withCredentials: true })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  let startPromise: Promise<void> | null = null;
  function ensureStarted() {
    if (connection.state === signalR.HubConnectionState.Connected) return Promise.resolve();
    if (!startPromise) startPromise = connection.start();
    return startPromise;
  }

  function on<Args extends unknown[]>(name: string, cb: (...args: Args) => void): () => void {
    const handler = (...args: Args) => cb(...args);
    connection.on(name, handler);
    return () => connection.off(name, handler);
  }

  singleton = {
    connection,
    async subscribeToChannel(channelId) {
      await ensureStarted();
      await connection.invoke('SubscribeToChannel', channelId);
    },
    async unsubscribeFromChannel(channelId) {
      if (connection.state !== signalR.HubConnectionState.Connected) return;
      await connection.invoke('UnsubscribeFromChannel', channelId);
    },
    async sendMessage(channelId, clientMessageId, content, replyToId, attachmentIds) {
      await ensureStarted();
      return connection.invoke<SendMessageResponse>('SendMessage', {
        channelId, clientMessageId, content, replyToId, attachmentIds,
      });
    },
    async deleteMessage(messageId) {
      await ensureStarted();
      return connection.invoke<{ ok: boolean; error?: string }>('DeleteMessage', messageId);
    },
    async editMessage(req) {
      await ensureStarted();
      return connection.invoke<EditMessageResponse>('EditMessage', req);
    },
    onMessageCreated: (cb) => on<[MessageDto]>('MessageCreated', cb),
    onMessageDeleted: (cb) => on<[string, number]>('MessageDeleted', cb),
    onMessageEdited: (cb) => on<[string, number, string, string]>('MessageEdited', cb),
    onChannelMemberJoined: (cb) => on<[string, ChannelMemberSummary]>('ChannelMemberJoined', cb),
    onChannelMemberLeft: (cb) => on<[string, string]>('ChannelMemberLeft', cb),
    onChannelMemberRoleChanged: (cb) => on<[string, string, string]>('ChannelMemberRoleChanged', cb),
    onRemovedFromChannel: (cb) => on<[string, string]>('RemovedFromChannel', cb),
    onChannelDeleted: (cb) => on<[string]>('ChannelDeleted', cb),
    onInvitationReceived: (cb) => on<[InvitationDto]>('InvitationReceived', cb),
    onFriendRequestReceived: (cb) => on<[FriendRequestDto]>('FriendRequestReceived', cb),
    onFriendRequestDecided: (cb) => on<[string, string]>('FriendRequestDecided', cb),
    onFriendRemoved: (cb) => on<[string]>('FriendRemoved', cb),
    onBlocked: (cb) => on<[string]>('Blocked', cb),
    async heartbeat(state) {
      if (connection.state !== signalR.HubConnectionState.Connected) return;
      await connection.invoke('Heartbeat', state);
    },
    async markRead(channelId, lastMessageId) {
      await ensureStarted();
      await connection.invoke('MarkRead', channelId, lastMessageId);
    },
    onPresenceChanged: (cb) => on<[string, PresenceState]>('PresenceChanged', cb),
    onUnreadChanged: (cb) => on<[string, number]>('UnreadChanged', cb),
    onForceLogout: (cb) => on<[string]>('ForceLogout', cb),
  };

  return singleton;
}

export function disposeHubClient() {
  if (singleton) {
    void singleton.connection.stop();
    singleton = null;
  }
}
