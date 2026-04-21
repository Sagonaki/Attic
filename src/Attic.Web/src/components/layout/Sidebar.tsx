import { motion } from 'motion/react';
import { Sidebar as SidebarIcon, ArrowUpDown, Users, LogOut } from 'lucide-react';
import { View, ChatCategory, Room, Contact } from '../../types';
import { Button } from '../ui';

interface SidebarProps {
  isCollapsed: boolean;
  onToggleCollapse: () => void;
  searchQuery: string;
  onSearchChange: (query: string) => void;
  chatCategory: ChatCategory;
  rooms: Room[];
  contacts: Contact[];
  activeRoomId?: string;
  activeContactId?: string;
  onRoomSelect: (id: string) => void;
  onContactSelect: (id: string) => void;
  onCreateRoom: () => void;
  onSortToggle: () => void;
  sortBy: string;
  roomsPage: number;
  totalPages: number;
  onPageChange: (page: number | ((p: number) => number)) => void;
}

export const Sidebar = ({
  isCollapsed,
  onToggleCollapse,
  searchQuery,
  onSearchChange,
  chatCategory,
  rooms,
  contacts,
  activeContactId,
  onContactSelect,
  onCreateRoom,
  onSortToggle,
  sortBy,
  roomsPage,
  totalPages,
  onPageChange
}: SidebarProps) => {
  return (
    <motion.aside 
      animate={{ width: isCollapsed ? 0 : 260 }}
      className="bg-slate-50 border-r border-slate-200 flex flex-col shrink-0 relative overflow-hidden group/sidebar"
    >
      <div className="p-4 flex flex-col gap-6 min-w-[260px]">
        <div className="flex items-center justify-between gap-4">
          <div className="relative flex-1">
            <input 
              placeholder="Search..." 
              value={searchQuery}
              onChange={(e) => onSearchChange(e.target.value)}
              className="w-full bg-white border border-slate-200 rounded-lg px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <span className="absolute right-3 top-2.5 text-[10px] font-bold text-slate-300">⌘K</span>
          </div>
          <button 
            onClick={onToggleCollapse}
            className="p-2 rounded-md hover:bg-slate-200 text-slate-400 transition-colors"
            title={isCollapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            <SidebarIcon className="w-5 h-5" />
          </button>
        </div>

        <div className="flex flex-col gap-6 overflow-y-auto max-h-[calc(100vh-250px)]">
          {chatCategory !== 'personal' && (
            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between px-1">
                <label className="text-[10px] font-black uppercase text-slate-400 tracking-[0.2em]">
                  {chatCategory === 'public' ? 'Public' : 'Private'} Rooms
                </label>
                {chatCategory === 'public' && (
                  <button 
                    onClick={onSortToggle}
                    className="text-[10px] font-black text-blue-600 uppercase hover:underline flex items-center gap-1"
                  >
                    <ArrowUpDown className="w-3 h-3" />
                    {sortBy}
                  </button>
                )}
              </div>
              
              <div className="flex flex-col mt-1 space-y-1">
                {rooms.map(room => (
                  <div key={room.id} className="group/room relative">
                    <button className={`w-full flex items-center justify-between px-3 py-1.5 rounded-md transition-all text-sm ${room.name === 'engineering' || room.name === 'core-team' ? 'bg-blue-100 text-blue-700 font-bold' : 'text-slate-600 hover:bg-slate-200'}`}>
                      <div className="flex items-center gap-2 overflow-hidden">
                        <span className="shrink-0">{room.type === 'public' ? '#' : '🔒'}</span>
                        <span className="truncate">{room.name}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        {room.memberCount && (
                          <span className="text-[10px] text-slate-400 font-bold flex items-center gap-0.5">
                            <Users className="w-3 h-3" /> {room.memberCount}
                          </span>
                        )}
                        {room.unreadCount && (
                          <span className="bg-blue-600 text-white text-[10px] font-bold px-1.5 py-0.5 rounded-full min-w-[20px] text-center">
                            {room.unreadCount}
                          </span>
                        )}
                      </div>
                    </button>
                    
                    <button 
                      className="absolute right-0 top-1/2 -translate-y-1/2 p-1.5 bg-white shadow-lg border border-slate-100 rounded-md text-red-500 opacity-0 group-hover/room:opacity-100 group-hover/room:-right-2 transition-all hover:bg-red-50 z-10"
                      title="Leave Room"
                      onClick={(e) => {
                        e.stopPropagation();
                        if (confirm(`Leave room #${room.name}?`)) {
                          // Logic
                        }
                      }}
                    >
                      <LogOut className="w-3.5 h-3.5" />
                    </button>
                  </div>
                ))}
              </div>

              {chatCategory === 'public' && totalPages > 1 && (
                <div className="flex items-center justify-between mt-4 px-1">
                  <button 
                    disabled={roomsPage === 1}
                    onClick={() => onPageChange(p => p - 1)}
                    className="text-[10px] font-bold text-slate-400 hover:text-slate-900 disabled:opacity-30 uppercase tracking-widest"
                  >
                    Prev
                  </button>
                  <span className="text-[10px] font-black text-slate-300 tracking-tighter">{roomsPage} / {totalPages}</span>
                  <button 
                    disabled={roomsPage === totalPages}
                    onClick={() => onPageChange(p => p + 1)}
                    className="text-[10px] font-bold text-slate-400 hover:text-slate-900 disabled:opacity-30 uppercase tracking-widest"
                  >
                    Next
                  </button>
                </div>
              )}
            </div>
          )}

          {chatCategory === 'personal' && (
            <div className="flex flex-col gap-2">
              <label className="text-[10px] font-black uppercase text-slate-400 tracking-[0.2em] px-1">
                Direct Messages
              </label>
              <div className="flex flex-col gap-1">
                {contacts.map(contact => (
                  <button 
                    key={contact.id} 
                    onClick={() => onContactSelect(contact.id)}
                    className={`flex items-center justify-between px-3 py-2 rounded-md transition-colors text-sm ${
                      activeContactId === contact.id ? 'bg-blue-100 text-blue-700 font-bold' : 'text-slate-600 hover:bg-slate-200'
                    }`}
                  >
                    <span className="flex items-center gap-3 font-semibold">
                      <span className={`w-2 h-2 rounded-full ${
                        contact.status === 'online' ? 'bg-green-500' : 
                        contact.status === 'afk' ? 'bg-orange-400' : 'bg-slate-300'
                      }`}></span>
                      {contact.name}
                    </span>
                    {contact.unreadCount && (
                      <span className="bg-red-500 text-white text-[10px] font-bold px-1.5 py-0.5 rounded-full">
                        {contact.unreadCount}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>

        {chatCategory !== 'personal' && (
          <div className="mt-auto pt-4">
            <Button variant="secondary" className="w-full" onClick={onCreateRoom}>
              + Create room
            </Button>
          </div>
        )}
      </div>
    </motion.aside>
  );
};
