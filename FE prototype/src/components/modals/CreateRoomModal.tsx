import { motion } from 'motion/react';
import { X, Plus, Hash, Lock } from 'lucide-react';
import { Button, Input } from '../ui';

interface CreateRoomModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCreate: () => void;
}

export const CreateRoomModal = ({
  isOpen,
  onClose,
  onCreate
}: CreateRoomModalProps) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} onClick={onClose} className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm" />
      <motion.div initial={{ scale: 0.9, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} className="relative w-full max-w-lg bg-white rounded-3xl shadow-2xl overflow-hidden flex flex-col">
        <div className="p-8 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 bg-blue-600 rounded-2xl flex items-center justify-center text-white shadow-lg">
              <Plus className="w-6 h-6" />
            </div>
            <h2 className="text-2xl font-black text-slate-900 uppercase tracking-tighter">Create New Room</h2>
          </div>
          <button onClick={onClose} className="p-2 hover:bg-slate-200 rounded-full transition-all"><X className="w-6 h-6" /></button>
        </div>

        <div className="p-8 space-y-6">
          <div className="grid grid-cols-2 gap-4">
            <button className="flex flex-col items-center gap-4 p-6 rounded-3xl border-4 border-blue-600 bg-blue-50/50 group transition-all">
              <div className="w-16 h-16 bg-blue-600 rounded-2xl flex items-center justify-center text-white shadow-lg shadow-blue-200">
                <Hash className="w-8 h-8" />
              </div>
              <span className="font-black uppercase tracking-widest text-xs text-blue-600">Public Room</span>
            </button>
            <button className="flex flex-col items-center gap-4 p-6 rounded-3xl border-4 border-transparent hover:border-slate-200 bg-slate-50 group transition-all">
              <div className="w-16 h-16 bg-slate-200 rounded-2xl flex items-center justify-center text-slate-400 group-hover:bg-slate-900 group-hover:text-white transition-all">
                <Lock className="w-8 h-8" />
              </div>
              <span className="font-black uppercase tracking-widest text-xs text-slate-400 group-hover:text-slate-900 transition-all">Private Room</span>
            </button>
          </div>

          <div className="space-y-4">
             <Input label="Room Name" placeholder="e.g. backend-devs" />
             <div className="flex flex-col gap-1 w-full">
                <label className="text-[10px] font-black uppercase text-slate-400 tracking-[0.2em] mb-1">Description (Optional)</label>
                <textarea className="w-full px-4 py-3 bg-white border border-slate-200 rounded-xl text-sm font-semibold focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all placeholder:text-slate-300 min-h-[100px]" placeholder="What is this room about?"></textarea>
             </div>
          </div>
        </div>

        <div className="p-8 bg-slate-50 border-t border-slate-100 flex gap-4">
          <Button className="flex-1 py-4" onClick={onCreate}>Create Room Instance</Button>
          <Button variant="ghost" className="px-8 py-4" onClick={onClose}>Cancel</Button>
        </div>
      </motion.div>
    </div>
  );
};
