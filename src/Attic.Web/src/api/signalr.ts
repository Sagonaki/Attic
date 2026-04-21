import * as signalR from '@microsoft/signalr';
import type { MessageDto, SendMessageResponse } from '../types';

export interface HubClient {
  connection: signalR.HubConnection;
  subscribeToChannel(channelId: string): Promise<void>;
  unsubscribeFromChannel(channelId: string): Promise<void>;
  sendMessage(channelId: string, clientMessageId: string, content: string): Promise<SendMessageResponse>;
  onMessageCreated(cb: (m: MessageDto) => void): () => void;
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
    onMessageCreated(cb) {
      const handler = (m: MessageDto) => cb(m);
      connection.on('MessageCreated', handler);
      return () => connection.off('MessageCreated', handler);
    },
  };

  return singleton;
}

export function disposeHubClient() {
  if (singleton) {
    void singleton.connection.stop();
    singleton = null;
  }
}
