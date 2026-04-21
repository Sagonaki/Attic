import { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';

// --- Types & Constants ---
import { 
  AuthMode, 
  View, 
  ChatCategory, 
  Room, 
  Contact, 
  Message, 
  Session, 
  FriendRequest 
} from './types';
import { EMOJIS, LOGO_URL } from './constants';

// --- Components ---
import { Auth } from './components/auth/Auth';
import { Header } from './components/layout/Header';
import { Sidebar } from './components/layout/Sidebar';
import { ChatWindow } from './components/chat/ChatWindow';
import { RoomDetails } from './components/chat/RoomDetails';
import { ProfileView } from './components/profile/ProfileView';
import { SessionsView } from './components/sessions/SessionsView';
import { ContactsView } from './components/contacts/ContactsView';
import { ManageRoomModal } from './components/modals/ManageRoomModal';
import { CreateRoomModal } from './components/modals/CreateRoomModal';

export default function App() {
  // State
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [authMode, setAuthMode] = useState<AuthMode>('signin');
  const [activeView, setActiveView] = useState<View>('chat');
  const [chatCategory, setChatCategory] = useState<ChatCategory>('public');
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);
  const [activeContactId, setActiveContactId] = useState<string>('c1');
  const [isManageRoomOpen, setIsManageRoomOpen] = useState(false);
  const [isCreateRoomOpen, setIsCreateRoomOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<'members' | 'admins' | 'banned' | 'invitations' | 'settings'>('settings');
  const [searchQuery, setSearchQuery] = useState('');
  const [inviteUsername, setInviteUsername] = useState('');
  const [inviteMessage, setInviteMessage] = useState('');
  const [messageInput, setMessageInput] = useState('');
  const [oldPassword, setOldPassword] = useState('********');
  const [newPassword, setNewPassword] = useState('********');
  const [replyingTo, setReplyingTo] = useState<Message | null>(null);
  const [editingMessage, setEditingMessage] = useState<Message | null>(null);
  const [typingUser, setTypingUser] = useState<string | null>(null);
  const [unreadNewMessages, setUnreadNewMessages] = useState(0);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [showEmojiPicker, setShowEmojiPicker] = useState(false);
  const [selectedSessions, setSelectedSessions] = useState<string[]>([]);
  const [isChatSearchVisible, setIsChatSearchVisible] = useState(false);
  const [roomSearchQuery, setRoomSearchQuery] = useState('');
  const [attachmentComment, setAttachmentComment] = useState('');
  const [roomsSortBy, setRoomsSortBy] = useState<'name' | 'members'>('name');
  const [roomsPage, setRoomsPage] = useState(1);
  const [bannedContactIds, setBannedContactIds] = useState<string[]>([]);
  const roomsPerPage = 10;
  
  const chatBodyRef = useRef<HTMLDivElement>(null);
  const isNearBottomRef = useRef(true);

  const [selectedAttachment, setSelectedAttachment] = useState<File | null>(null);
  const isChatFrozen = chatCategory === 'personal' && activeContactId && bannedContactIds.includes(activeContactId);

  const [messages, setMessages] = useState<Message[]>([
    { id: 'm1', sender: 'Bob', time: '10:21', content: 'Hello team', type: 'text' },
    { id: 'm2', sender: 'Alice', time: '10:22', content: 'Uploading spec', type: 'text' },
    { id: 'm3', sender: 'You', time: '10:23', content: 'Here\'s the file', type: 'file', fileName: 'spec-v3.pdf' },
    { id: 'm4', sender: 'Carol', time: '10:25', content: 'Can we make this private?', type: 'reply', replyToId: 'm1', replyToSender: 'Bob', replyToContent: 'Hello team' },
  ]);

  // Simulation: Typing
  useEffect(() => {
    if (!isLoggedIn) return;
    const interval = setInterval(() => {
      if (Math.random() > 0.7) {
        const users = ['Alice', 'Bob', 'Carol'];
        const randomUser = users[Math.floor(Math.random() * users.length)];
        setTypingUser(randomUser);
        setTimeout(() => setTypingUser(null), 3000);
      }
    }, 8000);
    return () => clearInterval(interval);
  }, [isLoggedIn]);

  // Simulation: New Messages
  useEffect(() => {
    if (!isLoggedIn) return;
    const interval = setInterval(() => {
      const messages_pool = [
        "Check out the new design docs!",
        "Anyone up for a quick sync?",
        "I'll be OOO for the rest of the day.",
        "Just pushed some fixes to main.",
        "Let's grab virtual coffee later. ☕",
        "Has anyone seen the logs for production?",
        "The latest build looks stable. 🚀"
      ];
      const senders = ['Alice', 'Bob', 'Carol'];
      const sender = senders[Math.floor(Math.random() * senders.length)];
      const content = messages_pool[Math.floor(Math.random() * messages_pool.length)];
      
      const newMessage: Message = {
        id: `m-sim-${Date.now()}`,
        sender,
        time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        content,
        type: 'text'
      };

      setMessages(prev => [...prev, newMessage]);
      
      if (!isNearBottomRef.current) {
        setUnreadNewMessages(prev => prev + 1);
      }
    }, 10000);
    return () => clearInterval(interval);
  }, [isLoggedIn]);

  // Smart Scroll
  useEffect(() => {
    if (isNearBottomRef.current && chatBodyRef.current) {
      chatBodyRef.current.scrollTo({
        top: chatBodyRef.current.scrollHeight,
        behavior: 'smooth'
      });
      setUnreadNewMessages(0);
    }
  }, [messages]);

  const handleScroll = () => {
    if (!chatBodyRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = chatBodyRef.current;
    
    // Check if near bottom
    const isBottom = scrollHeight - scrollTop - clientHeight < 100;
    isNearBottomRef.current = isBottom;
    if (isBottom) setUnreadNewMessages(0);

    // Infinite Scroll (Load more at top)
    if (scrollTop === 0 && !isLoadingMore) {
      loadMoreMessages();
    }
  };

  const loadMoreMessages = () => {
    setIsLoadingMore(true);
    // Simulate API delay
    setTimeout(() => {
      const oldMessages: Message[] = Array.from({ length: 10 }).map((_, i) => ({
        id: `old-${Date.now()}-${i}`,
        sender: ['Alice', 'Bob', 'Carol'][Math.floor(Math.random() * 3)],
        time: 'Yesterday',
        content: `Older message archive ${i + 1} - This is part of the infinite scroll simulation content.`,
        type: 'text'
      }));
      
      const container = chatBodyRef.current;
      const prevHeight = container?.scrollHeight || 0;

      setMessages(prev => [...oldMessages, ...prev]);
      
      // Preserve scroll position
      setTimeout(() => {
        if (container) {
          container.scrollTop = container.scrollHeight - prevHeight;
        }
        setIsLoadingMore(false);
      }, 0);
    }, 1000);
  };

  const [bannedUsers, setBannedUsers] = useState([
    { name: 'mike', by: 'alice', date: '2026-04-18 13:25' },
    { name: 'eve', by: 'dave', date: '2026-04-18 13:40' },
  ]);

  const [publicRooms, setPublicRooms] = useState<Room[]>([
    { id: 'r1', name: 'general', type: 'public', unreadCount: 3, memberCount: 156, createdAt: '2026-01-01' },
    { id: 'r2', name: 'engineering', type: 'public', memberCount: 42, createdAt: '2026-02-15' },
    { id: 'r3', name: 'random', type: 'public', memberCount: 89, createdAt: '2026-03-10' },
    { id: 'r6', name: 'design', type: 'public', memberCount: 23, createdAt: '2026-04-01' },
    { id: 'r7', name: 'marketing', type: 'public', memberCount: 12, createdAt: '2026-04-05' },
    { id: 'r8', name: 'sales', type: 'public', memberCount: 5, createdAt: '2026-04-10' },
    { id: 'r9', name: 'hr', type: 'public', memberCount: 8, createdAt: '2026-04-12' },
    { id: 'r10', name: 'legal', type: 'public', memberCount: 4, createdAt: '2026-04-15' },
    { id: 'r11', name: 'support', type: 'public', memberCount: 31, createdAt: '2026-04-18' },
    { id: 'r12', name: 'devops', type: 'public', memberCount: 19, createdAt: '2026-04-19' },
    { id: 'r13', name: 'security', type: 'public', memberCount: 56, createdAt: '2026-04-20' },
    { id: 'r14', name: 'product', type: 'public', memberCount: 27, createdAt: '2026-04-20' },
  ]);

  const [privateRooms, setPrivateRooms] = useState<Room[]>([
    { id: 'r4', name: 'core-team', type: 'private', unreadCount: 1, memberCount: 5 },
    { id: 'r5', name: 'ops', type: 'private', memberCount: 3 },
  ]);

  const filteredRooms = (chatCategory === 'public' ? publicRooms : privateRooms)
    .filter(room => room.name.toLowerCase().includes(searchQuery.toLowerCase()))
    .sort((a, b) => {
      if (roomsSortBy === 'name') return a.name.localeCompare(b.name);
      if (roomsSortBy === 'members') return (b.memberCount || 0) - (a.memberCount || 0);
      return 0;
    });

  const paginatedRooms = filteredRooms.slice((roomsPage - 1) * roomsPerPage, roomsPage * roomsPerPage);
  const totalPages = Math.ceil(filteredRooms.length / roomsPerPage);

  const contacts: Contact[] = [
    { id: 'c1', name: 'Alice', status: 'online' },
    { id: 'c2', name: 'Bob', status: 'afk' },
    { id: 'c3', name: 'Carol', status: 'offline', unreadCount: 2 },
  ];

  const sessions: Session[] = [
    { id: 's1', device: 'MacBook Pro', browser: 'Chrome 123.0', ip: '192.168.1.1', location: 'London, UK', lastActive: 'Active Now', isCurrent: true },
    { id: 's2', device: 'iPhone 15', browser: 'Safari Mobile', ip: '82.10.4.55', location: 'Paris, FR', lastActive: '2 hours ago' },
    { id: 's3', device: 'Windows Desktop', browser: 'Edge 121.0', ip: '201.55.22.11', location: 'Berlin, DE', lastActive: '3 days ago' },
  ];

  const friendRequests: FriendRequest[] = [
    { id: 'f1', username: 'Mallory', content: 'Hey, let\'s chat about the project!', status: 'pending' },
    { id: 'f2', username: 'Oscar', content: 'Add me! I have some ideas.', status: 'pending' },
    { id: 'f3', username: 'Sybil', content: 'Testing the scroll...', status: 'pending' },
  ];

  const sentFriendRequests: FriendRequest[] = [
    { id: 'sf1', username: 'Trudy', status: 'pending' },
    { id: 'sf2', username: 'Victor', status: 'accepted' },
  ];

  const sentInvitations = [
    { username: 'charlie', status: 'pending', date: '2026-04-19 10:00' },
    { username: 'eve', status: 'accepted', date: '2026-04-18 14:30' },
    { username: 'frank', status: 'pending', date: '2026-04-20 09:15' },
  ];

  const handleSendMessage = () => {
    if (!messageInput.trim() && !selectedAttachment) return;
    if (isChatFrozen) return;

    if (editingMessage) {
      setMessages(prev => prev.map(m => 
        m.id === editingMessage.id 
          ? { ...m, content: messageInput, isEdited: true } 
          : m
      ));
      setEditingMessage(null);
    } else {
      const newMessage: Message = {
        id: `m${Date.now()}`,
        sender: 'You',
        time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        content: messageInput,
        type: selectedAttachment ? 'file' : (replyingTo ? 'reply' : 'text'),
        ...(selectedAttachment && {
          fileName: selectedAttachment.name,
          fileComment: attachmentComment
        }),
        ...(replyingTo && {
          replyToId: replyingTo.id,
          replyToSender: replyingTo.sender,
          replyToContent: replyingTo.content,
        })
      };
      setMessages(prev => [...prev, newMessage]);
      setReplyingTo(null);
      setSelectedAttachment(null);
      setAttachmentComment('');
    }
    setMessageInput('');
  };

  if (!isLoggedIn) {
     return <Auth mode={authMode} onModeChange={setAuthMode} onLogin={() => setIsLoggedIn(true)} />;
  }

  return (
    <div className="h-screen flex flex-col bg-slate-50 overflow-hidden">
      <Header 
        activeView={activeView} 
        chatCategory={chatCategory} 
        onViewChange={setActiveView} 
        onCategoryChange={setChatCategory}
        onSignOut={() => setIsLoggedIn(false)}
        logoUrl={LOGO_URL}
      />

      <div className="flex flex-1 overflow-hidden relative">
        <AnimatePresence mode="wait">
          {activeView === 'chat' ? (
            <motion.div 
              key="chat-view"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="flex flex-1 overflow-hidden"
            >
              <Sidebar 
                isCollapsed={isSidebarCollapsed}
                onToggleCollapse={() => setIsSidebarCollapsed(!isSidebarCollapsed)}
                searchQuery={searchQuery}
                onSearchChange={setSearchQuery}
                chatCategory={chatCategory}
                rooms={paginatedRooms}
                contacts={contacts.filter(c => c.name.toLowerCase().includes(searchQuery.toLowerCase()))}
                activeContactId={activeContactId}
                onRoomSelect={() => {}} 
                onContactSelect={setActiveContactId}
                onCreateRoom={() => setIsCreateRoomOpen(true)}
                onSortToggle={() => setRoomsSortBy(roomsSortBy === 'name' ? 'members' : 'name')}
                sortBy={roomsSortBy}
                roomsPage={roomsPage}
                totalPages={totalPages}
                onPageChange={setRoomsPage}
              />

              <ChatWindow 
                chatCategory={chatCategory}
                activeContact={contacts.find(c => c.id === activeContactId)}
                isSidebarCollapsed={isSidebarCollapsed}
                onToggleSidebar={() => setIsSidebarCollapsed(!isSidebarCollapsed)}
                isChatSearchVisible={isChatSearchVisible}
                onToggleChatSearch={() => setIsChatSearchVisible(!isChatSearchVisible)}
                onManageRoom={() => setIsManageRoomOpen(true)}
                onLeaveRoom={() => {}}
                onBlockUser={() => {
                   if (activeContactId) {
                      setBannedContactIds(prev => prev.includes(activeContactId) ? prev : [...prev, activeContactId]);
                   }
                }}
                onViewProfile={() => {}}
                messages={messages}
                chatBodyRef={chatBodyRef}
                onScroll={handleScroll}
                isLoadingMore={isLoadingMore}
                typingUser={typingUser}
                unreadNewMessages={unreadNewMessages}
                onScrollToBottom={() => {
                  if (chatBodyRef.current) {
                    chatBodyRef.current.scrollTo({ top: chatBodyRef.current.scrollHeight, behavior: 'smooth' });
                    setUnreadNewMessages(0);
                  }
                }}
                isChatFrozen={isChatFrozen}
                replyingTo={replyingTo}
                editingMessage={editingMessage}
                onCancelContext={() => {
                  setReplyingTo(null);
                  setEditingMessage(null);
                  setMessageInput('');
                }}
                messageInput={messageInput}
                onMessageInputChange={setMessageInput}
                onSendMessage={handleSendMessage}
                onDeleteMessage={(id) => setMessages(prev => prev.filter(m => m.id !== id))}
                onStartEdit={(msg) => {
                  setEditingMessage(msg);
                  setReplyingTo(null);
                  setMessageInput(msg.content);
                }}
                onStartReply={(msg) => {
                  setReplyingTo(msg);
                  setEditingMessage(null);
                  setMessageInput('');
                }}
                selectedAttachment={selectedAttachment}
                onSelectAttachment={setSelectedAttachment}
                attachmentComment={attachmentComment}
                onAttachmentCommentChange={setAttachmentComment}
                showEmojiPicker={showEmojiPicker}
                onToggleEmojiPicker={setShowEmojiPicker}
                emojis={EMOJIS}
              />

              {chatCategory !== 'personal' && <RoomDetails />}
            </motion.div>
          ) : activeView === 'profile' ? (
            <ProfileView 
              onSignOut={() => setIsLoggedIn(false)}
              oldPassword={oldPassword}
              setOldPassword={setOldPassword}
              newPassword={newPassword}
              setNewPassword={setNewPassword}
            />
          ) : activeView === 'sessions' ? (
            <SessionsView 
              sessions={sessions}
              selectedSessions={selectedSessions}
              onToggleSession={(id) => {
                 setSelectedSessions(prev => 
                    prev.includes(id) ? prev.filter(s => s !== id) : [...prev, id]
                 );
              }}
              onRevokeSessions={() => {
                 if (confirm(`Revoke ${selectedSessions.length} sessions?`)) {
                    setSelectedSessions([]);
                 }
              }}
            />
          ) : (
            <ContactsView 
              incomingRequests={friendRequests} 
              outgoingRequests={sentFriendRequests} 
            />
          )}
        </AnimatePresence>
      </div>

      <ManageRoomModal 
        isOpen={isManageRoomOpen}
        onClose={() => setIsManageRoomOpen(false)}
        activeTab={activeTab}
        onTabChange={setActiveTab}
        inviteUsername={inviteUsername}
        onInviteUsernameChange={setInviteUsername}
        inviteMessage={inviteMessage}
        onInviteMessageChange={setInviteMessage}
        bannedUsers={bannedUsers}
        sentInvitations={sentInvitations}
      />

      <CreateRoomModal 
        isOpen={isCreateRoomOpen}
        onClose={() => setIsCreateRoomOpen(false)}
        onCreate={() => setIsCreateRoomOpen(false)}
      />
    </div>
  );
}
