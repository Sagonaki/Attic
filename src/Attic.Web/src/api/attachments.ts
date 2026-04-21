import type { UploadAttachmentResponse } from '../types';

export const attachmentsApi = {
  async upload(file: File, comment?: string): Promise<UploadAttachmentResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (comment) form.append('comment', comment);

    const r = await fetch(new URL('/api/attachments', window.location.origin), {
      method: 'POST',
      credentials: 'include',
      body: form,
    });
    if (!r.ok) {
      let code = 'upload_failed';
      try { code = (await r.json()).code ?? code; } catch { /* ignore */ }
      throw new Error(code);
    }
    return r.json();
  },
  downloadUrl(attachmentId: string): string {
    return `/api/attachments/${attachmentId}`;
  },
};
