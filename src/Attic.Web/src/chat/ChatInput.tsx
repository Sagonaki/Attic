import { useRef, useState } from 'react';
import { useUploadAttachments } from './useUploadAttachments';
import { ReplyPreview } from './ReplyPreview';

type OnSend = (
  content: string,
  opts?: { replyToId?: number | null; attachmentIds?: string[] }
) => void | Promise<void>;

export interface ChatInputProps {
  onSend: OnSend;
  replyTo?: { messageId: number; snippet: string } | null;
  onCancelReply?: () => void;
}

export function ChatInput({ onSend, replyTo, onCancelReply }: ChatInputProps) {
  const [content, setContent] = useState('');
  const { pending, upload, clear, removeOne } = useUploadAttachments();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const readyAttachments = pending.filter(p => p.status === 'done' && p.attachment).map(p => p.attachment!.id);
  const isBusy = pending.some(p => p.status === 'uploading');

  async function submit() {
    if (isBusy) return;
    if (!content.trim() && readyAttachments.length === 0) return;
    await onSend(content.trim(), { replyToId: replyTo?.messageId ?? null, attachmentIds: readyAttachments });
    setContent('');
    clear();
    onCancelReply?.();
  }

  function onPaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const files = Array.from(e.clipboardData?.files ?? []);
    if (files.length > 0) { e.preventDefault(); void upload(files); }
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    const files = Array.from(e.dataTransfer?.files ?? []);
    if (files.length > 0) void upload(files);
  }

  return (
    <div onDragOver={e => e.preventDefault()} onDrop={onDrop}>
      {replyTo && <ReplyPreview replySnippet={replyTo.snippet} onCancel={() => onCancelReply?.()} />}
      {pending.length > 0 && (
        <div className="flex flex-wrap gap-2 p-2 bg-slate-50 border-t">
          {pending.map(p => (
            <div key={p.id} className="flex items-center gap-1 px-2 py-1 bg-white border rounded text-xs">
              <span className={p.status === 'error' ? 'text-red-600' : p.status === 'uploading' ? 'text-slate-500' : ''}>
                {p.file.name}
              </span>
              {p.status === 'uploading' && <span className="text-slate-400">…</span>}
              <button onClick={() => removeOne(p.id)} className="text-slate-400">×</button>
            </div>
          ))}
        </div>
      )}
      <div className="flex items-end gap-2 p-3 border-t bg-white">
        <input ref={fileInputRef} type="file" multiple className="hidden"
               onChange={e => { if (e.target.files) { void upload(Array.from(e.target.files)); e.target.value = ''; } }} />
        <button onClick={() => fileInputRef.current?.click()} className="text-slate-500 hover:text-slate-700 pb-2">📎</button>
        <textarea
          className="flex-1 border rounded px-3 py-2 resize-none"
          rows={1}
          placeholder="Type a message…"
          value={content}
          onChange={e => setContent(e.target.value)}
          onPaste={onPaste}
          onKeyDown={e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(); }
          }}
        />
        <button onClick={submit} disabled={isBusy}
                className="px-3 py-2 bg-blue-600 text-white rounded disabled:opacity-50">
          Send
        </button>
      </div>
    </div>
  );
}
