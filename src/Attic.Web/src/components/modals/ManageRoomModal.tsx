import { motion } from 'motion/react';
import { X, Users, Shield, Lock, Bell, Settings, Search } from 'lucide-react';
import { Button, Input } from '../ui';
import { BannedUser, SentInvitation } from '../../types';

interface ManageRoomModalProps {
  isOpen: boolean;
  onClose: () => void;
  activeTab: 'members' | 'admins' | 'banned' | 'invitations' | 'settings';
  onTabChange: (tab: any) => void;
  inviteUsername: string;
  onInviteUsernameChange: (val: string) => void;
  inviteMessage: string;
  onInviteMessageChange: (val: string) => void;
  bannedUsers: BannedUser[];
  sentInvitations: SentInvitation[];
}

export const ManageRoomModal = ({
  isOpen,
  onClose,
  activeTab,
  onTabChange,
  inviteUsername,
  onInviteUsernameChange,
  inviteMessage,
  onInviteMessageChange,
  bannedUsers,
  sentInvitations
}: ManageRoomModalProps) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} onClick={onClose} className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm" />
      <motion.div initial={{ scale: 0.9, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} className="relative w-full max-w-4xl bg-white rounded-3xl shadow-2xl overflow-hidden flex flex-col max-h-[85vh]">
        <div className="p-8 border-b border-slate-100 flex items-center justify-between bg-slate-50/50">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 bg-slate-900 rounded-2xl flex items-center justify-center text-white shadow-lg">
              <Settings className="w-6 h-6" />
            </div>
            <div>
              <h2 className="text-2xl font-black text-slate-900 uppercase tracking-tighter">Manage Room</h2>
              <p className="text-xs font-bold text-slate-400 uppercase tracking-[0.2em]">Engineering Room #042</p>
            </div>
          </div>
          <button onClick={onClose} className="p-2 hover:bg-slate-200 rounded-full transition-all"><X className="w-6 h-6" /></button>
        </div>

        <div className="flex flex-1 overflow-hidden">
          <aside className="w-64 bg-slate-50 border-r border-slate-100 p-6 flex flex-col gap-2">
            {[
              { id: 'settings', label: 'General', icon: Settings },
              { id: 'members', label: 'Members', icon: Users },
              { id: 'admins', label: 'Admins', icon: Shield },
              { id: 'banned', label: 'Banned', icon: Lock },
              { id: 'invitations', label: 'Invitations', icon: Bell },
            ].map(tab => (
              <button key={tab.id} onClick={() => onTabChange(tab.id)} className={`flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-black uppercase tracking-wider transition-all ${activeTab === tab.id ? 'bg-blue-600 text-white shadow-lg shadow-blue-200 translate-x-2' : 'text-slate-400 hover:bg-white hover:text-slate-600'}`}>
                <tab.icon className="w-4 h-4" /> {tab.label}
              </button>
            ))}
          </aside>

          <main className="flex-1 overflow-y-auto p-10">
            {activeTab === 'settings' && (
              <div className="space-y-8 max-w-xl">
                <Input label="Room Name" defaultValue="engineering-room" />
                <Input label="Room Description" defaultValue="Classic room description here..." />
                <div className="pt-4 flex gap-4">
                  <Button className="px-8 py-3">Save Changes</Button>
                  <Button variant="ghost" onClick={onClose}>Discard</Button>
                </div>
              </div>
            )}

            {activeTab === 'invitations' && (
              <div className="space-y-8">
                <div className="bg-blue-50 p-6 rounded-2xl border border-blue-100">
                  <h4 className="text-[10px] font-black text-blue-600 uppercase tracking-[0.2em] mb-4">Invite user</h4>
                  <div className="grid grid-cols-2 gap-4 mb-4">
                    <Input label="Username" placeholder="e.g. charlie" value={inviteUsername} onChange={(e: any) => onInviteUsernameChange(e.target.value)} />
                    <Input label="Message" placeholder="Join our room!" value={inviteMessage} onChange={(e: any) => onInviteMessageChange(e.target.value)} />
                  </div>
                  <Button className="w-full">Send Invitation</Button>
                </div>
                <div className="space-y-4">
                   <h4 className="text-[10px] font-black text-slate-400 uppercase tracking-[0.2em]">Sent Invitations</h4>
                   {sentInvitations.map((inv, idx) => (
                    <div key={idx} className="flex items-center justify-between p-4 bg-slate-50 rounded-xl border border-slate-100">
                      <div>
                        <div className="text-sm font-black text-slate-900 uppercase">{inv.username}</div>
                        <div className="text-[10px] font-bold text-slate-400">{inv.date}</div>
                      </div>
                      <span className={`text-[10px] font-black uppercase px-3 py-1 rounded-full ${inv.status === 'accepted' ? 'bg-green-100 text-green-600' : 'bg-orange-100 text-orange-600'}`}>{inv.status}</span>
                    </div>
                   ))}
                </div>
              </div>
            )}

            {activeTab === 'banned' && (
              <div className="space-y-6">
                <h4 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Restricted Access</h4>
                <div className="divide-y divide-slate-100">
                  {bannedUsers.map((user, idx) => (
                    <div key={idx} className="py-4 flex items-center justify-between group">
                      <div className="flex items-center gap-4">
                        <div className="w-10 h-10 bg-red-100 rounded-lg flex items-center justify-center text-red-600"><Lock className="w-5 h-5" /></div>
                        <div>
                          <div className="text-sm font-black text-slate-900 uppercase">{user.name}</div>
                          <div className="text-[10px] font-bold text-slate-400">Banned by {user.by} • {user.date}</div>
                        </div>
                      </div>
                      <Button variant="ghost" className="opacity-0 group-hover:opacity-100 px-4 py-2 border-red-200 text-red-600 hover:bg-red-50">Revoke Ban</Button>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {activeTab === 'members' && (
              <div className="space-y-6">
                 <div className="flex items-center justify-between">
                    <h4 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Current Members (42)</h4>
                    <div className="relative"><input placeholder="Search members..." className="bg-slate-100 border-none rounded-lg px-4 py-2 text-xs outline-none" /><Search className="absolute right-3 top-2.5 w-3 h-3 text-slate-400" /></div>
                 </div>
                 <div className="grid grid-cols-2 gap-4">
                    {['Alice', 'Bob', 'Charlie', 'Dave', 'Eve', 'Frank'].map(name => (
                      <div key={name} className="flex items-center justify-between p-4 bg-slate-50 rounded-2xl border border-slate-100 hover:border-slate-300 transition-all cursor-pointer group">
                        <div className="flex items-center gap-3">
                          <div className="w-8 h-8 rounded-lg bg-white border border-slate-200 flex items-center justify-center font-black text-xs text-slate-900">{name[0]}</div>
                          <span className="text-sm font-bold text-slate-700">{name}</span>
                        </div>
                        <button className="opacity-0 group-hover:opacity-100 text-[10px] font-black uppercase text-red-500 hover:underline">Kick</button>
                      </div>
                    ))}
                 </div>
              </div>
            )}
          </main>
        </div>
      </motion.div>
    </div>
  );
};
