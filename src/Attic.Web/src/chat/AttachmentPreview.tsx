import type { AttachmentDto } from '../types';
import { attachmentsApi } from '../api/attachments';

export function AttachmentPreview({ attachment }: { attachment: AttachmentDto }) {
  const href = attachmentsApi.downloadUrl(attachment.id);
  if (attachment.contentType.startsWith('image/')) {
    return (
      <a href={href} target="_blank" rel="noreferrer" className="block max-w-sm my-1">
        <img src={href} alt={attachment.originalFileName} className="rounded border max-h-48" />
      </a>
    );
  }
  const kb = Math.max(1, Math.round(attachment.sizeBytes / 1024));
  return (
    <a href={href} target="_blank" rel="noreferrer"
       className="inline-flex items-center gap-2 mt-1 px-2 py-1 border rounded text-xs text-foreground hover:bg-muted/30">
      📎 {attachment.originalFileName} <span className="text-muted-foreground/70">· {kb} KB</span>
    </a>
  );
}
