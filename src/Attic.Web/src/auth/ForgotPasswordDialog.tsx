import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { authExtrasApi } from '../api/authExtras';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { toast } from 'sonner';

export function ForgotPasswordDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [email, setEmail] = useState('');
  const mutation = useMutation({
    mutationFn: () => authExtrasApi.forgotPassword({ email }),
    onSuccess: () => {
      toast.success('If the email is registered, a new password has been generated.', {
        description: 'In development, check the server console for the new password.',
      });
      setEmail('');
      onClose();
    },
  });

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><KeyRound className="h-4 w-4" />Forgot password?</DialogTitle>
          <DialogDescription>
            A new password will be generated and logged to the server console
            (this is the MVP — a real deployment would email it).
          </DialogDescription>
        </DialogHeader>
        <Input type="email" placeholder="Your email" value={email}
               onChange={e => setEmail(e.target.value)} autoComplete="email" />
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button onClick={() => mutation.mutate()} disabled={!email || mutation.isPending}>
            {mutation.isPending ? 'Sending…' : 'Reset password'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
