import { useCallback, useState } from 'react';
import { toast } from 'sonner';
import { attachmentsApi } from '../api/attachments';
import type { UploadAttachmentResponse } from '../types';

export interface PendingUpload {
  id: string;          // local UUID before upload resolves
  file: File;
  status: 'uploading' | 'done' | 'error';
  attachment?: UploadAttachmentResponse;
  error?: string;
}

export function useUploadAttachments() {
  const [pending, setPending] = useState<PendingUpload[]>([]);

  const upload = useCallback(async (files: File[]) => {
    const startBatch: PendingUpload[] = files.map(f => ({
      id: crypto.randomUUID(), file: f, status: 'uploading',
    }));
    setPending(prev => [...prev, ...startBatch]);

    await Promise.all(startBatch.map(async p => {
      try {
        const resp = await attachmentsApi.upload(p.file);
        setPending(prev => prev.map(x =>
          x.id === p.id ? { ...x, status: 'done', attachment: resp } : x));
      } catch (e) {
        toast.error('Upload failed', { description: (e as Error).message });
        setPending(prev => prev.map(x =>
          x.id === p.id ? { ...x, status: 'error', error: (e as Error).message } : x));
      }
    }));
  }, []);

  const clear = useCallback(() => setPending([]), []);
  const removeOne = useCallback((id: string) => setPending(prev => prev.filter(p => p.id !== id)), []);

  return { pending, upload, clear, removeOne };
}
