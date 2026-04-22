import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Globe, Lock, MessageSquare, Plus, BookOpen, Mail, Users } from 'lucide-react';
import { useChannelList } from './useChannelList';
import { useOpenPersonalChat } from './useOpenPersonalChat';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { cn } from '@/lib/utils';

type Tab = 'public' | 'private' | 'personal';

const tabIcon: Record<Tab, typeof Globe> = {
  public: Globe,
  private: Lock,
  personal: MessageSquare,
};

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
    <aside className="w-64 border-r bg-card flex flex-col">
      <div className="p-3 border-b">
        <Tabs value={tab} onValueChange={v => setTab(v as Tab)}>
          <TabsList className="w-full grid grid-cols-3">
            {(['public', 'private', 'personal'] as const).map(k => {
              const Icon = tabIcon[k];
              return (
                <TabsTrigger key={k} value={k} className="gap-1.5">
                  <Icon className="h-3.5 w-3.5" />
                  <span className="capitalize">{k}</span>
                </TabsTrigger>
              );
            })}
          </TabsList>
        </Tabs>
      </div>

      <div className="p-2 border-b flex gap-1">
        {tab === 'public' && (
          <Button asChild variant="outline" size="sm" className="flex-1">
            <Link to="/catalog"><BookOpen className="h-3.5 w-3.5" />Catalog</Link>
          </Button>
        )}
        {tab === 'personal' ? (
          <Button variant="outline" size="sm" className="flex-1" onClick={promptAndOpen}>
            <Plus className="h-3.5 w-3.5" />Personal chat
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="flex-1" onClick={onCreate}>
            <Plus className="h-3.5 w-3.5" />New room
          </Button>
        )}
      </div>

      <ScrollArea className="flex-1">
        <ul>
          {isLoading && <li className="p-3 text-muted-foreground text-sm">Loading…</li>}
          {!isLoading && channels.length === 0 && (
            <li className="p-3 text-muted-foreground text-sm">No {tab} channels.</li>
          )}
          {channels.map(c => {
            const href = `/chat/${c.id}`;
            const active = pathname === href;
            return (
              <li key={c.id}>
                <Link
                  to={href}
                  className={cn(
                    'flex items-center justify-between px-3 py-2 text-sm hover:bg-accent hover:text-accent-foreground transition-colors',
                    active && 'bg-accent text-accent-foreground'
                  )}
                >
                  <span className="truncate">
                    {c.kind === 'personal'
                      ? (c.otherMemberUsername ?? 'Personal chat')
                      : (c.name ?? 'Channel')}
                  </span>
                  {c.unreadCount > 0 && (
                    <Badge variant="default" className="h-5 px-1.5">{c.unreadCount}</Badge>
                  )}
                </Link>
              </li>
            );
          })}
        </ul>
      </ScrollArea>

      <Separator />
      <div className="p-2 grid grid-cols-2 gap-1">
        <Button asChild variant="ghost" size="sm"><Link to="/contacts"><Users className="h-3.5 w-3.5" />Contacts</Link></Button>
        <Button asChild variant="ghost" size="sm"><Link to="/invitations"><Mail className="h-3.5 w-3.5" />Invites</Link></Button>
      </div>
    </aside>
  );
}
