import { RefObject } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  Hash, 
  User, 
  Search, 
  MoreVertical, 
  ChevronDown, 
  Paperclip, 
  X, 
  Reply, 
  Edit2, 
  Trash2,
  Lock,
  Sidebar as SidebarIcon,
  Settings,
  LogOut
} from 'lucide-react';
import { Message, ChatCategory, Contact } from '../../types';

interface ChatWindowProps {
  chatCategory: ChatCategory;
  activeContact?: Contact;
  isSidebarCollapsed: boolean;
  onToggleSidebar: () => void;
  isChatSearchVisible: boolean;
  onToggleChatSearch: () => void;
  onManageRoom: () => void;
  onLeaveRoom: () => void;
  onBlockUser: () => void;
  onViewProfile: () => void;
  messages: Message[];
  chatBodyRef: RefObject<HTMLDivElement | null>;
  onScroll: () => void;
  isLoadingMore: boolean;
  typingUser: string | null;
  unreadNewMessages: number;
  onScrollToBottom: () => void;
  isChatFrozen: boolean;
  replyingTo: Message | null;
  editingMessage: Message | null;
  onCancelContext: () => void;
  messageInput: string;
  onMessageInputChange: (val: string) => void;
  onSendMessage: () => void;
  onDeleteMessage: (id: string) => void;
  onStartEdit: (msg: Message) => void;
  onStartReply: (msg: Message) => void;
  selectedAttachment: File | null;
  onSelectAttachment: (file: File | null) => void;
  attachmentComment: string;
  onAttachmentCommentChange: (val: string) => void;
  showEmojiPicker: boolean;
  onToggleEmojiPicker: (val: boolean) => void;
  emojis: string[];
}

export const ChatWindow = ({
  chatCategory,
  activeContact,
  isSidebarCollapsed,
  onToggleSidebar,
  isChatSearchVisible,
  onToggleChatSearch,
  onManageRoom,
  onLeaveRoom,
  onBlockUser,
  onViewProfile,
  messages,
  chatBodyRef,
  onScroll,
  isLoadingMore,
  typingUser,
  unreadNewMessages,
  onScrollToBottom,
  isChatFrozen,
  replyingTo,
  editingMessage,
  onCancelContext,
  messageInput,
  onMessageInputChange,
  onSendMessage,
  onDeleteMessage,
  onStartEdit,
  onStartReply,
  selectedAttachment,
  onSelectAttachment,
  attachmentComment,
  onAttachmentCommentChange,
  showEmojiPicker,
  onToggleEmojiPicker,
  emojis
}: ChatWindowProps) => {
  const currentUser = 'You';
  const admins = ['Alice', 'Dave'];
  const isCurrentUserAdmin = admins.includes(currentUser);

  return (
    <main className="flex-1 flex flex-col bg-white overflow-hidden shadow-inner">
      {/* Header */}
      <div className="h-16 px-6 border-b border-slate-100 flex items-center justify-between shrink-0 bg-white/80 backdrop-blur-md sticky top-0 z-10 transition-all duration-300">
        <div className="flex items-center gap-4">
          <button onClick={onToggleSidebar} className="p-1.5 rounded-md hover:bg-slate-100 text-slate-400">
            <SidebarIcon className="w-5 h-5" />
          </button>
          
          <div className="flex items-baseline gap-2">
            <h2 className="font-bold text-slate-800 flex items-center gap-2">
              {chatCategory === 'personal' ? (
                <User className="w-4 h-4 text-blue-500" />
              ) : (
                <Hash className="w-4 h-4 text-blue-500" />
              )}
              {chatCategory === 'personal' ? activeContact?.name || 'Direct Message' : 'engineering-room'}
            </h2>
            <p className="text-xs text-slate-500 font-medium whitespace-nowrap overflow-hidden text-ellipsis max-w-[200px] sm:max-w-md italic">
              {chatCategory === 'personal' ? `Direct conversation with ${activeContact?.name}` : 'Classic room description here...'}
            </p>
          </div>
        </div>
        
        <div className="flex items-center gap-2">
          <div className={`flex items-center transition-all duration-300 overflow-hidden ${isChatSearchVisible ? 'w-48 opacity-100 mr-2' : 'w-0 opacity-0'}`}>
            <input 
              type="text"
              placeholder="Search in chat..."
              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs focus:ring-2 focus:ring-blue-100 outline-none font-semibold"
              autoFocus
            />
          </div>
          <button 
            onClick={onToggleChatSearch}
            className={`p-2 rounded-full transition-colors ${isChatSearchVisible ? 'bg-blue-50 text-blue-600' : 'hover:bg-slate-100 text-slate-400'}`}
          >
            <Search className="w-5 h-5" />
          </button>
          
          <div className="relative group/menu">
            <button className="p-2 rounded-full hover:bg-slate-100 text-slate-400">
              <MoreVertical className="w-5 h-5" />
            </button>
            <div className="absolute right-0 top-full mt-1 w-48 bg-white border border-slate-200 rounded-xl shadow-xl py-2 invisible group-hover/menu:visible opacity-0 group-hover/menu:opacity-100 transition-all z-50">
              {chatCategory === 'personal' ? (
                <>
                  <button onClick={onViewProfile} className="w-full text-left px-4 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50 flex items-center gap-2">
                    <User className="w-4 h-4" /> View Profile
                  </button>
                  <button onClick={onBlockUser} className="w-full text-left px-4 py-2 text-xs font-bold text-red-600 hover:bg-red-50 flex items-center gap-2">
                    <Lock className="w-4 h-4" /> Block User
                  </button>
                </>
              ) : (
                <>
                  <button onClick={onManageRoom} className="w-full text-left px-4 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50 flex items-center gap-2">
                    <Settings className="w-4 h-4" /> Manage Room
                  </button>
                  <button onClick={onLeaveRoom} className="w-full text-left px-4 py-2 text-xs font-bold text-red-600 hover:bg-red-50 flex items-center gap-2">
                    <LogOut className="w-4 h-4" /> Leave Room
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Body */}
      <div 
        ref={chatBodyRef}
        onScroll={onScroll}
        className="flex-1 overflow-y-auto p-6 space-y-6 flex flex-col relative"
      >
        {isLoadingMore && (
          <div className="flex justify-center py-4">
            <div className="flex gap-1">
              <span className="w-1.5 h-1.5 bg-blue-600 rounded-full animate-bounce [animation-delay:-0.3s]"></span>
              <span className="w-1.5 h-1.5 bg-blue-600 rounded-full animate-bounce [animation-delay:-0.15s]"></span>
              <span className="w-1.5 h-1.5 bg-blue-600 rounded-full animate-bounce"></span>
            </div>
          </div>
        )}

        {messages.map((msg, i) => {
          const canDelete = msg.sender === currentUser || (chatCategory !== 'personal' && isCurrentUserAdmin);
          return (
            <div key={msg.id} className="flex flex-col gap-1 group relative">
              {i === 3 && (
                <div className="unread-divider">
                  <span>New Messages</span>
                </div>
              )}
              
              <div className={`flex gap-4 ${msg.sender === currentUser ? 'flex-row-reverse' : ''}`}>
                <div className={`w-8 h-8 rounded-lg flex items-center justify-center text-white font-black text-xs shrink-0 ${
                  msg.sender === 'Alice' ? 'bg-blue-500' : 
                  msg.sender === 'Bob' ? 'bg-orange-400' : 
                  msg.sender === 'Carol' ? 'bg-pink-500' : 'bg-slate-900'
                }`}>
                  {msg.sender[0]}
                </div>
                <div className={`relative ${msg.sender === currentUser ? 'text-right' : ''}`}>
                  <div className={`flex items-baseline gap-2 mb-1 ${msg.sender === currentUser ? 'flex-row-reverse' : ''}`}>
                    <span className="font-black text-sm text-slate-900">{msg.sender}</span>
                    <span className="text-[10px] font-bold text-slate-400">{msg.time}</span>
                    {msg.isEdited && <span className="text-[10px] font-bold text-slate-300 italic">(edited)</span>}
                  </div>

                  <div className={`relative group/msg-bubble mt-1 max-w-[500px] text-sm leading-relaxed ${
                    msg.sender === currentUser ? 'bg-slate-100 p-3 rounded-2xl rounded-tr-none inline-block text-slate-800 font-semibold shadow-sm' : 'text-slate-600 font-semibold'
                  }`}>
                    {msg.replyToId && (
                      <div className="mb-2 p-2 bg-slate-50/50 border-l-4 border-blue-200 rounded text-xs text-slate-500 italic flex flex-col gap-1 text-left">
                        <span className="font-black text-[10px] text-blue-600 uppercase not-italic tracking-wider">Reply to {msg.replyToSender}</span>
                        <span className="line-clamp-1 opacity-70">"{msg.replyToContent}"</span>
                      </div>
                    )}

                    {msg.type === 'file' ? (
                      <div className="flex flex-col gap-2">
                        <div className="p-3 bg-white/50 border border-slate-200 rounded-xl flex items-center gap-3 w-max text-slate-900">
                          <div className="p-2 bg-white rounded border border-slate-100 shadow-sm text-lg">📄</div>
                          <div>
                            <div className="text-xs font-black">{msg.fileName}</div>
                            <div className="text-[10px] text-slate-400 font-bold uppercase tracking-wider">File Attachment</div>
                          </div>
                        </div>
                        {msg.fileComment && <div className="text-xs text-slate-600 bg-slate-50/50 p-2 rounded-lg italic border-l-2 border-slate-200">{msg.fileComment}</div>}
                      </div>
                    ) : msg.content}

                    <div className={`absolute -top-4 opacity-0 group-hover/msg-bubble:opacity-100 transition-opacity flex items-center bg-white border border-slate-100 rounded-lg shadow-xl px-1 py-1 z-20 ${msg.sender === currentUser ? 'right-0' : 'left-0'}`}>
                      <button onClick={() => onStartReply(msg)} className="p-1.5 hover:bg-slate-50 text-slate-400 hover:text-blue-600 rounded transition-colors"><Reply className="w-3.5 h-3.5" /></button>
                      {msg.sender === currentUser && <button onClick={() => onStartEdit(msg)} className="p-1.5 hover:bg-slate-50 text-slate-400 hover:text-orange-500 rounded transition-colors"><Edit2 className="w-3.5 h-3.5" /></button>}
                      {canDelete && <button onClick={() => onDeleteMessage(msg.id)} className="p-1.5 hover:bg-slate-50 text-slate-400 hover:text-red-500 rounded transition-colors"><Trash2 className="w-3.5 h-3.5" /></button>}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          );
        })}

        {typingUser && (
          <div className="flex items-center gap-3 text-slate-400">
            <div className="flex gap-1 bg-slate-100 px-3 py-2 rounded-full scale-75 origin-left">
              <span className="w-1 h-1 bg-slate-400 rounded-full animate-bounce"></span>
              <span className="w-1 h-1 bg-slate-400 rounded-full animate-bounce [animation-delay:0.2s]"></span>
              <span className="w-1 h-1 bg-slate-400 rounded-full animate-bounce [animation-delay:0.4s]"></span>
            </div>
            <span className="text-[10px] font-black uppercase tracking-widest">{typingUser} is typing...</span>
          </div>
        )}
      </div>

      {unreadNewMessages > 0 && (
        <button onClick={onScrollToBottom} className="absolute bottom-24 left-1/2 -translate-x-1/2 z-30 bg-blue-600 text-white px-4 py-2 rounded-full shadow-xl flex items-center gap-2 border-2 border-white scale-90 hover:scale-100 transition-all font-black text-[10px] uppercase tracking-widest">
          <ChevronDown className="w-3 h-3" /> {unreadNewMessages} New Messages
        </button>
      )}

      {/* Footer */}
      <footer className={`bg-white px-6 py-4 flex flex-col gap-4 z-10 border-t border-slate-200 transition-all duration-200 ${isChatFrozen ? 'h-[140px]' : (replyingTo || editingMessage || selectedAttachment) ? 'h-auto min-h-[140px]' : 'h-[120px]'}`}>
        {isChatFrozen ? (
          <div className="flex-1 flex items-center justify-center bg-slate-50 border-2 border-dashed border-slate-200 rounded-2xl p-4">
            <div className="flex flex-col items-center gap-2">
              <Lock className="w-6 h-6 text-slate-300" />
              <p className="text-xs font-black text-slate-400 uppercase tracking-widest text-center">Conversation Frozen</p>
            </div>
          </div>
        ) : (
          <>
            {selectedAttachment && (
              <div className="flex flex-col gap-3 bg-blue-50/50 border border-blue-100 p-3 rounded-2xl">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 bg-white rounded-lg border border-blue-100 flex items-center justify-center text-xl shadow-sm">📄</div>
                    <div className="flex flex-col">
                      <span className="text-xs font-black text-slate-900 line-clamp-1">{selectedAttachment.name}</span>
                      <span className="text-[10px] font-bold text-slate-400">{(selectedAttachment.size / 1024).toFixed(1)} KB</span>
                    </div>
                  </div>
                  <button onClick={() => onSelectAttachment(null)} className="p-1 hover:bg-blue-100 rounded-full text-blue-600"><X className="w-4 h-4" /></button>
                </div>
                <input placeholder="Add comment..." value={attachmentComment} onChange={(e) => onAttachmentCommentChange(e.target.value)} className="bg-white/80 border border-blue-100 rounded-xl px-4 py-2 text-xs outline-none" />
              </div>
            )}
            <div className="flex items-start gap-4">
              <div className="flex gap-2 pt-2 relative">
                <button onClick={() => onToggleEmojiPicker(!showEmojiPicker)} className="p-2 hover:bg-slate-100 rounded-lg text-lg">😊</button>
                {showEmojiPicker && (
                  <div className="absolute bottom-full mb-2 left-0 bg-white border border-slate-200 rounded-xl shadow-2xl p-3 z-50 w-[240px] grid grid-cols-6 gap-1">
                    {emojis.map(e => <button key={e} onClick={() => { onMessageInputChange(messageInput + e); onToggleEmojiPicker(false); }} className="w-8 h-8 flex items-center justify-center hover:bg-slate-50 rounded-lg">{e}</button>)}
                  </div>
                )}
                <label className="p-2 hover:bg-slate-100 rounded-lg text-slate-400 cursor-pointer">
                  <Paperclip className="w-6 h-6" />
                  <input type="file" className="hidden" onChange={(e) => onSelectAttachment(e.target.files?.[0] || null)} />
                </label>
              </div>
              <div className="flex-1 relative">
                <AnimatePresence>
                  {(replyingTo || editingMessage) && (
                    <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: 10 }} className="absolute -top-10 left-0 right-0 bg-slate-50 border border-slate-200 rounded-t-xl px-4 py-2 flex items-center justify-between border-b-0">
                      <div className="flex items-center gap-2 overflow-hidden">
                        {replyingTo ? <><Reply className="w-3.5 h-3.5 text-blue-600" /><span className="text-[10px] font-black text-blue-600 uppercase">Replying to {replyingTo.sender}</span></> : <><Edit2 className="w-3.5 h-3.5 text-orange-500" /><span className="text-[10px] font-black text-orange-500 uppercase">Editing</span></>}
                      </div>
                      <button onClick={onCancelContext} className="p-1 hover:bg-slate-200 rounded text-slate-400"><X className="w-4 h-4" /></button>
                    </motion.div>
                  )}
                </AnimatePresence>
                <textarea 
                  placeholder={editingMessage ? "Edit message..." : "Type message..."}
                  value={messageInput}
                  onChange={(e) => onMessageInputChange(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); onSendMessage(); } }}
                  className={`w-full h-16 bg-slate-50 border border-slate-200 px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none font-black text-slate-700 ${(replyingTo || editingMessage) ? 'rounded-b-xl border-t-0' : 'rounded-xl'}`}
                />
              </div>
              <div className="pt-2">
                <button onClick={onSendMessage} disabled={!messageInput.trim() && !selectedAttachment} className="px-8 py-3 bg-blue-600 text-white text-xs font-black uppercase tracking-widest rounded-lg hover:bg-blue-700 shadow-lg shadow-blue-200 disabled:grayscale disabled:opacity-50">
                  {editingMessage ? 'Update' : 'Send'}
                </button>
              </div>
            </div>
          </>
        )}
      </footer>
    </main>
  );
};
