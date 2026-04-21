export interface MessageActionsMenuProps {
  isOwn: boolean;
  isAdmin: boolean;
  onEdit: () => void;
  onReply: () => void;
  onDelete: () => void;
  onClose: () => void;
}

export function MessageActionsMenu({ isOwn, isAdmin, onEdit, onReply, onDelete, onClose }: MessageActionsMenuProps) {
  return (
    <div className="absolute right-2 top-8 bg-white border rounded shadow z-10 text-sm"
         onMouseLeave={onClose}>
      <button className="block w-full text-left px-3 py-1 hover:bg-slate-100" onClick={onReply}>Reply</button>
      {isOwn && <button className="block w-full text-left px-3 py-1 hover:bg-slate-100" onClick={onEdit}>Edit</button>}
      {(isOwn || isAdmin) && (
        <button className="block w-full text-left px-3 py-1 hover:bg-slate-100 text-red-600" onClick={onDelete}>Delete</button>
      )}
    </div>
  );
}
