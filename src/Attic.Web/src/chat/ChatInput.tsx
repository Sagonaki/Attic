import { useState } from 'react';

interface Props {
  onSend: (content: string) => Promise<void>;
}

export function ChatInput({ onSend }: Props) {
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const content = text.trim();
    if (!content) return;
    setBusy(true);
    try {
      await onSend(content);
      setText('');
    } catch {
      // keep text so user can retry
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="flex gap-2 p-3 border-t bg-white">
      <textarea
        className="flex-1 border rounded px-3 py-2 resize-none"
        rows={2}
        placeholder="Write a message…"
        value={text}
        onChange={e => setText(e.target.value)}
        onKeyDown={e => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            void submit(e as unknown as React.FormEvent);
          }
        }}
        maxLength={3072}
      />
      <button type="submit" disabled={busy || text.trim() === ''}
              className="px-4 bg-blue-600 text-white rounded disabled:opacity-50">
        Send
      </button>
    </form>
  );
}
