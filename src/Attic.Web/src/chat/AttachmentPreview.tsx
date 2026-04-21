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
       className="inline-flex items-center gap-2 mt-1 px-2 py-1 border rounded text-xs text-slate-700 hover:bg-slate-50">
      📎 {attachment.originalFileName} <span className="text-slate-400">· {kb} KB</span>
    </a>
  );
}
