import { X, Reply } from 'lucide-react';
import { Button } from '@/components/ui/button';

export function ReplyPreview({ replySnippet, onCancel }: { replySnippet: string; onCancel: () => void }) {
  return (
    <div className="flex items-center justify-between px-3 py-1.5 bg-muted text-xs text-muted-foreground border-t border-b">
      <span className="flex items-center gap-2">
        <Reply className="h-3 w-3" />
        Replying to: <em className="text-foreground/80">{replySnippet}</em>
      </span>
      <Button variant="ghost" size="icon" className="h-5 w-5" onClick={onCancel}>
        <X className="h-3 w-3" />
      </Button>
    </div>
  );
}
