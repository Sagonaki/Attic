import { useRef, useState } from 'react';
import { Paperclip, Send, X, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { useUploadAttachments } from './useUploadAttachments';
import { ReplyPreview } from './ReplyPreview';
import { EmojiPickerPopover } from './EmojiPickerPopover';

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
        <div className="flex flex-wrap gap-2 p-2 bg-muted/50 border-t">
          {pending.map(p => (
            <Badge key={p.id} variant="secondary" className="gap-1 pr-1">
              <FileText className="h-3 w-3" />
              <span className="max-w-[12rem] truncate">{p.file.name}</span>
              {p.status === 'uploading' && <span className="text-muted-foreground">…</span>}
              {p.status === 'error' && <span className="text-destructive">!</span>}
              <button onClick={() => removeOne(p.id)} className="ml-1 rounded hover:bg-background/50 p-0.5">
                <X className="h-3 w-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}
      <div className="flex items-end gap-2 p-3 border-t bg-card">
        <input ref={fileInputRef} type="file" multiple className="hidden"
               onChange={e => { if (e.target.files) { void upload(Array.from(e.target.files)); e.target.value = ''; } }} />
        <EmojiPickerPopover onPick={emoji => setContent(c => c + emoji)} />
        <Button variant="ghost" size="icon" onClick={() => fileInputRef.current?.click()} aria-label="Attach file">
          <Paperclip className="h-4 w-4" />
        </Button>
        <Textarea
          className="flex-1 min-h-[40px] max-h-40 resize-none"
          rows={1}
          placeholder="Type a message…"
          value={content}
          onChange={e => setContent(e.target.value)}
          onPaste={onPaste}
          onKeyDown={e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(); }
          }}
        />
        <Button onClick={submit} disabled={isBusy} aria-label="Send message">
          <Send className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
