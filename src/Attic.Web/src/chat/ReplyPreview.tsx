export function ReplyPreview({ replySnippet, onCancel }: { replySnippet: string; onCancel: () => void }) {
  return (
    <div className="flex items-center justify-between px-3 py-1 bg-slate-100 border-t border-b text-xs text-slate-600">
      <span>Replying to: <em className="text-slate-500">{replySnippet}</em></span>
      <button onClick={onCancel} className="px-2 text-slate-500 hover:text-slate-700">×</button>
    </div>
  );
}
