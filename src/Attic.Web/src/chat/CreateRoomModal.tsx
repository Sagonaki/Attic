export function CreateRoomModal({ onClose }: { onClose: () => void }) {
  return <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
    <div className="bg-white rounded p-4">Create modal — see Task 40.</div>
  </div>;
}
