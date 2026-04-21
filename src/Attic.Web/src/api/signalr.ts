import * as signalR from '@microsoft/signalr';
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto } from '../types';

export interface HubClient {
  connection: signalR.HubConnection;
  subscribeToChannel(channelId: string): Promise<void>;
  unsubscribeFromChannel(channelId: string): Promise<void>;
  sendMessage(channelId: string, clientMessageId: string, content: string): Promise<SendMessageResponse>;
  deleteMessage(messageId: number): Promise<{ ok: boolean; error?: string }>;
  onMessageCreated(cb: (m: MessageDto) => void): () => void;
  onMessageDeleted(cb: (channelId: string, messageId: number) => void): () => void;
  onChannelMemberJoined(cb: (channelId: string, member: ChannelMemberSummary) => void): () => void;
  onChannelMemberLeft(cb: (channelId: string, userId: string) => void): () => void;
  onChannelMemberRoleChanged(cb: (channelId: string, userId: string, role: string) => void): () => void;
  onRemovedFromChannel(cb: (channelId: string, reason: string) => void): () => void;
  onChannelDeleted(cb: (channelId: string) => void): () => void;
  onInvitationReceived(cb: (invitation: InvitationDto) => void): () => void;
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
    async sendMessage(channelId, clientMessageId, content) {
      await ensureStarted();
      return connection.invoke<SendMessageResponse>('SendMessage', {
        channelId, clientMessageId, content, replyToId: null,
      });
    },
    async deleteMessage(messageId) {
      await ensureStarted();
      return connection.invoke<{ ok: boolean; error?: string }>('DeleteMessage', messageId);
    },
    onMessageCreated: (cb) => on<[MessageDto]>('MessageCreated', cb),
    onMessageDeleted: (cb) => on<[string, number]>('MessageDeleted', cb),
    onChannelMemberJoined: (cb) => on<[string, ChannelMemberSummary]>('ChannelMemberJoined', cb),
    onChannelMemberLeft: (cb) => on<[string, string]>('ChannelMemberLeft', cb),
    onChannelMemberRoleChanged: (cb) => on<[string, string, string]>('ChannelMemberRoleChanged', cb),
    onRemovedFromChannel: (cb) => on<[string, string]>('RemovedFromChannel', cb),
    onChannelDeleted: (cb) => on<[string]>('ChannelDeleted', cb),
    onInvitationReceived: (cb) => on<[InvitationDto]>('InvitationReceived', cb),
  };

  return singleton;
}

export function disposeHubClient() {
  if (singleton) {
    void singleton.connection.stop();
    singleton = null;
  }
}
