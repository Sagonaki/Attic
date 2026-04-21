import { User, Mail, Shield, Smartphone, Lock, Trash2 } from 'lucide-react';
import { Button, Input } from '../ui';

interface ProfileViewProps {
  onSignOut: () => void;
  oldPassword: string;
  setOldPassword: (val: string) => void;
  newPassword: string;
  setNewPassword: (val: string) => void;
}

export const ProfileView = ({
  onSignOut,
  oldPassword,
  setOldPassword,
  newPassword,
  setNewPassword
}: ProfileViewProps) => {
  return (
    <div className="flex-1 overflow-y-auto bg-slate-50/50 p-12">
      <div className="max-w-4xl mx-auto">
        <div className="flex items-end justify-between mb-12">
          <div className="flex items-center gap-8">
            <div className="w-40 h-40 bg-slate-900 rounded-3xl flex items-center justify-center text-5xl font-black text-white shadow-[12px_12px_0px_0px_rgba(30,41,59,1)] border-4 border-slate-900">
              JD
            </div>
            <div className="flex flex-col gap-2">
              <h1 className="text-6xl font-black text-slate-900 uppercase tracking-tighter">John Doe</h1>
              <span className="text-xl font-bold text-slate-400 uppercase tracking-widest flex items-center gap-2">
                <Shield className="w-5 h-5" /> Account Owner
              </span>
            </div>
          </div>
          <Button variant="danger" onClick={onSignOut}>Sign Out</Button>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          {/* Identity */}
          <div className="bg-white border-2 border-slate-900 rounded-3xl overflow-hidden shadow-[12px_12px_0px_0px_rgba(15,23,42,1)] p-8">
            <div className="flex flex-col gap-6">
              <div className="flex items-center gap-3 mb-2">
                <User className="w-5 h-5 text-slate-400" />
                <h3 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Identity</h3>
              </div>
              <div className="grid grid-cols-2 gap-6">
                <Input label="First Name" defaultValue="John" />
                <Input label="Last Name" defaultValue="Doe" />
              </div>
              <Input label="Display Name" defaultValue="JD_Admin" />
              <div className="flex justify-end pt-2">
                 <Button variant="secondary" className="px-8">Update Identity</Button>
              </div>
            </div>
          </div>

          {/* Contact */}
          <div className="bg-white border-2 border-slate-900 rounded-3xl overflow-hidden shadow-[12px_12px_0px_0px_rgba(15,23,42,1)] p-8">
            <div className="flex flex-col gap-6">
              <div className="flex items-center gap-3 mb-2">
                <Mail className="w-5 h-5 text-slate-400" />
                <h3 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Communication</h3>
              </div>
              <Input label="Primary Email" defaultValue="john.doe@company.com" />
              <Input label="Phone Number" placeholder="+1 (555) 000-0000" />
              <div className="flex justify-end pt-2">
                 <Button variant="secondary" className="px-8">Verify Email</Button>
              </div>
            </div>
          </div>
        </div>

        {/* Security */}
        <div className="mt-12 bg-white border-2 border-slate-900 rounded-3xl overflow-hidden shadow-[12px_12px_0px_0px_rgba(15,23,42,1)] p-8">
          <div className="flex flex-col gap-6">
            <div className="flex items-center gap-3 mb-2">
              <Lock className="w-5 h-5 text-slate-400" />
              <h3 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Security</h3>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <Input 
                label="Current Password" 
                type="password" 
                value={oldPassword} 
                onChange={(e: any) => setOldPassword(e.target.value)} 
              />
              <Input 
                label="New Password" 
                type="password" 
                value={newPassword} 
                onChange={(e: any) => setNewPassword(e.target.value)} 
              />
            </div>
            <div className="flex justify-end pt-2">
               <Button variant="secondary" className="px-8">Update Password</Button>
            </div>
          </div>
        </div>

        {/* Danger Zone */}
        <div className="mt-12 bg-white border-2 border-red-200 rounded-3xl overflow-hidden shadow-[12px_12px_0px_0px_rgba(254,226,226,1)] p-8">
          <div className="flex flex-col gap-6">
            <div className="flex items-center gap-3">
              <Trash2 className="w-5 h-5 text-red-500" />
              <h3 className="text-xl font-black text-red-600 uppercase tracking-tighter">Danger Zone</h3>
            </div>
            
            <div className="bg-red-50/50 rounded-2xl p-6 border border-red-100/50">
              <p className="text-sm font-bold text-red-800 mb-4 uppercase tracking-wide">If you delete your account:</p>
              <ul className="space-y-3">
                {[
                  'Your account is removed',
                  'Only chat rooms owned by you are deleted',
                  'All messages, files, and images in those deleted rooms are deleted permanently',
                  'Membership in other rooms is removed'
                ].map((item, idx) => (
                  <li key={idx} className="flex gap-3 text-red-700/80 text-sm font-semibold">
                    <span className="w-5 h-5 bg-red-100 rounded-full flex items-center justify-center shrink-0 text-[10px] text-red-600 font-bold">{idx + 1}</span>
                    {item}
                  </li>
                ))}
              </ul>
            </div>

            <div className="flex justify-end pt-2">
               <Button variant="danger" className="px-12 py-4 border-red-600 text-red-600 hover:bg-red-600 hover:text-white transition-all transform hover:-translate-y-1 active:translate-y-0" onClick={() => { if(confirm('Are you absolutely sure you want to delete your account? This action cannot be undone.')) onSignOut(); }}>
                  Permanently Delete Account
               </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
