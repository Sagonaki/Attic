import { Users, Mail, MessageSquare, ChevronRight } from 'lucide-react';
import { FriendRequest } from '../../types';
import { Button, Input } from '../ui';

interface ContactsViewProps {
  incomingRequests: FriendRequest[];
  outgoingRequests: FriendRequest[];
}

export const ContactsView = ({ incomingRequests, outgoingRequests }: ContactsViewProps) => {
  return (
    <div className="flex-1 overflow-y-auto bg-slate-50/50 p-12">
      <div className="max-w-4xl mx-auto">
        <div className="flex flex-col gap-4 mb-12">
          <h1 className="text-6xl font-black text-slate-900 uppercase tracking-tighter">Contacts</h1>
          <p className="text-xl font-bold text-slate-400 uppercase tracking-widest flex items-center gap-2">
            <Users className="w-5 h-5" /> Connect with other users on Attic
          </p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12">
          {/* Send Request */}
          <div className="flex flex-col gap-8">
            <div className="bg-white border-2 border-slate-900 rounded-3xl p-8 shadow-[12px_12px_0px_0px_rgba(15,23,42,1)]">
              <div className="flex items-center gap-3 mb-8">
                <div className="w-10 h-10 bg-blue-600 rounded-xl flex items-center justify-center text-white shadow-lg">
                  <Mail className="w-5 h-5" />
                </div>
                <h3 className="text-xl font-black text-slate-900 uppercase tracking-tighter">Send Friend Request</h3>
              </div>
              
              <div className="space-y-6">
                <Input label="Username" placeholder="e.g. johndoe" defaultValue="" />
                <Input label="Add Message (Optional)" placeholder="Hi! I'd like to connect..." defaultValue="" />
                <Button className="w-full py-4 mt-2">Send Invitation</Button>
              </div>
            </div>

            {/* Sent Invitations Section */}
            <div className="flex flex-col gap-6">
              <div className="flex items-center justify-between px-2">
                <h3 className="text-xs font-black uppercase text-slate-400 tracking-[0.2em] flex items-center gap-2">
                  Invitations Sent by Me ({outgoingRequests.length})
                </h3>
              </div>

              <div className="space-y-4">
                {outgoingRequests.map(request => (
                  <div key={request.id} className="bg-white border border-slate-200 p-4 rounded-2xl flex items-center gap-4 group hover:border-slate-400 transition-all shadow-sm">
                    <div className="w-10 h-10 bg-slate-50 rounded-xl flex items-center justify-center text-slate-400 font-black text-xs group-hover:bg-slate-900 group-hover:text-white transition-all">
                      {request.username[0]}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center justify-between">
                        <span className="font-black text-slate-900 uppercase tracking-tighter truncate mr-2">{request.username}</span>
                        <span className={`text-[9px] font-black uppercase px-2 py-0.5 rounded-full tracking-widest ${
                          request.status === 'accepted' ? 'bg-green-100 text-green-600' : 'bg-orange-100 text-orange-600'
                        }`}>
                          {request.status}
                        </span>
                      </div>
                    </div>
                    {request.status === 'pending' && (
                      <button className="text-[10px] font-black text-red-500 uppercase hover:underline opacity-0 group-hover:opacity-100 transition-opacity">
                        Cancel
                      </button>
                    )}
                  </div>
                ))}
                {outgoingRequests.length === 0 && (
                  <p className="text-xs text-slate-400 font-bold italic px-2">No invitations sent yet.</p>
                )}
              </div>
            </div>

            <div className="bg-blue-600 rounded-3xl p-8 text-white shadow-[12px_12px_0px_0px_rgba(37,99,235,0.3)] mt-2">
              <h4 className="text-2xl font-black uppercase tracking-tighter mb-4">Connect Smarter</h4>
              <p className="text-blue-100 text-sm font-bold leading-relaxed mb-6">Building a network in Attic allows you to create private channels and share files securely with specific team members.</p>
              <div className="flex items-center gap-4">
                <div className="flex -space-x-3">
                  {[1,2,3,4].map(i => <div key={i} className="w-10 h-10 rounded-xl border-2 border-blue-600 bg-blue-500 overflow-hidden flex items-center justify-center font-black text-[10px]">U{i}</div>)}
                </div>
                <span className="text-xs font-black uppercase tracking-widest">+124 others online</span>
              </div>
            </div>
          </div>

          {/* Pending Requests (Incoming) */}
          <div className="flex flex-col gap-6">
            <div className="flex items-center justify-between px-2">
              <h3 className="text-xs font-black uppercase text-slate-400 tracking-[0.2em] flex items-center gap-2">
                Pending Decisions ({incomingRequests.length})
              </h3>
            </div>

            <div className="space-y-4">
              {incomingRequests.map(request => (
                <div key={request.id} className="bg-white border border-slate-200 p-6 rounded-2xl flex items-start gap-4 group hover:border-slate-400 transition-all shadow-sm ">
                  <div className="w-12 h-12 bg-slate-100 rounded-xl flex items-center justify-center text-slate-400 font-black text-sm group-hover:bg-slate-900 group-hover:text-white transition-all">
                    {request.username[0]}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between mb-1">
                      <span className="font-black text-slate-900 uppercase tracking-tighter">{request.username}</span>
                      <span className="text-[10px] font-black uppercase text-blue-600 tracking-widest bg-blue-50 px-2 py-0.5 rounded-full">New Request</span>
                    </div>
                    <p className="text-xs text-slate-500 font-bold line-clamp-1 italic mb-4">"{request.content || 'No message provided'}"</p>
                    <div className="flex gap-2">
                      <Button className="flex-1 py-2 text-[10px]">Accept</Button>
                      <Button variant="ghost" className="flex-1 py-2 text-[10px]">Decline</Button>
                    </div>
                  </div>
                </div>
              ))}
              {incomingRequests.length === 0 && (
                <p className="text-xs text-slate-400 font-bold italic px-2">No pending invitations.</p>
              )}
              
              <button className="w-full py-4 bg-slate-100 rounded-2xl text-[10px] font-black uppercase tracking-[0.2em] text-slate-400 hover:bg-slate-200 transition-all flex items-center justify-center gap-2">
                View All Network History <ChevronRight className="w-3 h-3" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
