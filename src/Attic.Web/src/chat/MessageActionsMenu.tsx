import { MoreHorizontal, Reply, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export function MessageActionsMenu({
  isOwn, isAdmin, onEdit, onReply, onDelete,
}: {
  isOwn: boolean;
  isAdmin: boolean;
  onEdit: () => void;
  onReply: () => void;
  onDelete: () => void;
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity">
          <MoreHorizontal className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={onReply}><Reply className="h-4 w-4" />Reply</DropdownMenuItem>
        {isOwn && <DropdownMenuItem onClick={onEdit}><Pencil className="h-4 w-4" />Edit</DropdownMenuItem>}
        {(isOwn || isAdmin) && (
          <DropdownMenuItem onClick={onDelete} className="text-destructive focus:text-destructive">
            <Trash2 className="h-4 w-4" />Delete
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
