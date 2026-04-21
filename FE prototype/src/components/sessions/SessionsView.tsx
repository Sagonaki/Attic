import { Smartphone, Monitor, Globe, Shield, Trash2 } from 'lucide-react';
import { Session } from '../../types';
import { Button } from '../ui';

interface SessionsViewProps {
  sessions: Session[];
  selectedSessions: string[];
  onToggleSession: (id: string) => void;
  onRevokeSessions: () => void;
}

export const SessionsView = ({
  sessions,
  selectedSessions,
  onToggleSession,
  onRevokeSessions
}: SessionsViewProps) => {
  return (
    <div className="flex-1 overflow-y-auto bg-slate-50/50 p-12">
      <div className="max-w-5xl mx-auto">
        <div className="flex items-end justify-between mb-12">
          <div className="flex flex-col gap-4">
            <h1 className="text-6xl font-black text-slate-900 uppercase tracking-tighter">Active Sessions</h1>
            <p className="text-xl font-bold text-slate-400 uppercase tracking-widest flex items-center gap-2">
              <Shield className="w-5 h-5" /> Authorized Devices
            </p>
          </div>
          <Button 
            variant="danger" 
            disabled={selectedSessions.length === 0}
            onClick={onRevokeSessions}
          >
            Revoke Selected ({selectedSessions.length})
          </Button>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {sessions.map(session => (
            <div 
              key={session.id}
              onClick={() => onToggleSession(session.id)}
              className={`group relative bg-white border-2 rounded-3xl p-8 cursor-pointer transition-all duration-300 transform hover:-translate-y-2 ${
                selectedSessions.includes(session.id) 
                ? 'border-red-500 shadow-[12px_12px_0px_0px_rgba(239,68,68,1)]' 
                : 'border-slate-900 shadow-[12px_12px_0px_0px_rgba(15,23,42,1)] hover:shadow-[16px_16px_0px_0px_rgba(15,23,42,1)]'
              }`}
            >
              <div className="flex items-start justify-between mb-6">
                <div className={`p-4 rounded-2xl ${session.device.includes('Mac') || session.device.includes('Windows') ? 'bg-slate-900 text-white' : 'bg-blue-600 text-white'}`}>
                  {session.device.includes('Mac') || session.device.includes('Windows') ? <Monitor className="w-8 h-8" /> : <Smartphone className="w-8 h-8" />}
                </div>
                {session.isCurrent && (
                  <span className="bg-green-100 text-green-700 text-[10px] font-black uppercase tracking-[0.2em] px-3 py-1.5 rounded-full border border-green-200">Current</span>
                )}
              </div>

              <div className="space-y-4">
                <h3 className="text-2xl font-black text-slate-900 tracking-tighter uppercase">{session.device}</h3>
                
                <div className="space-y-2">
                  <div className="flex items-center gap-3 text-sm text-slate-500 font-bold uppercase tracking-wider">
                    <Globe className="w-4 h-4" /> {session.browser}
                  </div>
                  <div className="flex items-center gap-3 text-sm text-slate-400 font-bold tracking-tight">
                    {session.ip} • {session.location}
                  </div>
                </div>

                <div className="pt-4 border-t border-slate-100 flex items-center justify-between">
                  <span className="text-[10px] font-black text-slate-400 uppercase tracking-widest">{session.lastActive}</span>
                  <div className={`w-6 h-6 rounded-lg border-2 flex items-center justify-center transition-all ${selectedSessions.includes(session.id) ? 'bg-red-500 border-red-500 text-white' : 'border-slate-200 text-transparent'}`}>
                    <Trash2 className="w-3.5 h-3.5" />
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
