import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useChannelList } from './useChannelList';
import { useOpenPersonalChat } from './useOpenPersonalChat';

type Tab = 'public' | 'private' | 'personal';

export function Sidebar({ onCreate }: { onCreate: () => void }) {
  const { data, isLoading } = useChannelList();
  const [tab, setTab] = useState<Tab>('public');
  const { pathname } = useLocation();
  const openChat = useOpenPersonalChat();

  function promptAndOpen() {
    const username = window.prompt('Open personal chat with (username):');
    if (username && username.trim().length >= 3) openChat(username.trim());
  }

  const channels = (data ?? []).filter(c => c.kind === tab);

  return (
    <aside className="w-64 border-r bg-white flex flex-col">
      <nav className="flex border-b text-sm">
        {(['public', 'private', 'personal'] as const).map(k => (
          <button key={k} onClick={() => setTab(k)}
                  className={`flex-1 py-2 ${tab === k ? 'font-semibold border-b-2 border-blue-600' : 'text-slate-500'}`}>
            {k[0].toUpperCase() + k.slice(1)}
          </button>
        ))}
      </nav>
      <div className="p-2 border-b flex gap-2">
        <Link to="/catalog" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Catalog
        </Link>
        {tab === 'personal' ? (
          <button onClick={promptAndOpen} className="flex-1 text-xs px-2 py-1 border rounded hover:bg-slate-50">
            + New personal chat
          </button>
        ) : (
          <button onClick={onCreate} className="flex-1 text-xs px-2 py-1 border rounded hover:bg-slate-50">
            + New room
          </button>
        )}
      </div>
      <ul className="flex-1 overflow-y-auto">
        {isLoading && <li className="p-3 text-slate-400 text-sm">Loading…</li>}
        {!isLoading && channels.length === 0 && (
          <li className="p-3 text-slate-400 text-sm">No {tab} channels.</li>
        )}
        {channels.map(c => {
          const href = `/chat/${c.id}`;
          const active = pathname === href;
          return (
            <li key={c.id}>
              <Link to={href}
                    className={`block px-3 py-2 text-sm truncate ${active ? 'bg-blue-50 text-blue-700' : 'hover:bg-slate-50'}`}>
                {c.name ?? 'Personal chat'}
                {c.unreadCount > 0 && (
                  <span className="ml-2 text-xs bg-blue-600 text-white rounded-full px-2">{c.unreadCount}</span>
                )}
              </Link>
            </li>
          );
        })}
      </ul>
      <div className="p-2 border-t flex gap-2">
        <Link to="/contacts" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Contacts
        </Link>
        <Link to="/invitations" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Invitations
        </Link>
      </div>
    </aside>
  );
}
