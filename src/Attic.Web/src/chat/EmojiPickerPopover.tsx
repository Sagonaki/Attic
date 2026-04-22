import { useEffect, useRef, useState } from 'react';
import Picker from '@emoji-mart/react';
import data from '@emoji-mart/data';
import { Smile } from 'lucide-react';
import { useTheme } from '@/theme/ThemeProvider';
import { Button } from '@/components/ui/button';

export function EmojiPickerPopover({ onPick }: { onPick: (emoji: string) => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const { resolvedTheme } = useTheme();

  useEffect(() => {
    if (!open) return;
    function onClickOutside(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    function onEsc(e: KeyboardEvent) { if (e.key === 'Escape') setOpen(false); }
    document.addEventListener('mousedown', onClickOutside);
    document.addEventListener('keydown', onEsc);
    return () => {
      document.removeEventListener('mousedown', onClickOutside);
      document.removeEventListener('keydown', onEsc);
    };
  }, [open]);

  return (
    <div className="relative" ref={ref}>
      <Button variant="ghost" size="icon" onClick={() => setOpen(v => !v)} aria-label="Add emoji">
        <Smile className="h-4 w-4" />
      </Button>
      {open && (
        <div className="absolute bottom-full mb-2 right-0 z-50">
          <Picker
            data={data}
            theme={resolvedTheme}
            onEmojiSelect={(e: { native: string }) => { onPick(e.native); setOpen(false); }}
            previewPosition="none"
            skinTonePosition="none"
          />
        </div>
      )}
    </div>
  );
}
