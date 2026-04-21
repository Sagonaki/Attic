import { View, ChatCategory } from '../../types';

interface HeaderProps {
  activeView: View;
  chatCategory: ChatCategory;
  onViewChange: (view: View) => void;
  onCategoryChange: (category: ChatCategory) => void;
  onSignOut: () => void;
  logoUrl: string;
}

export const Header = ({ 
  activeView, 
  chatCategory, 
  onViewChange, 
  onCategoryChange, 
  onSignOut,
  logoUrl 
}: HeaderProps) => {
  return (
    <header className="h-[60px] bg-white border-b border-slate-200 px-6 flex items-center justify-between shrink-0 z-30">
      <div className="flex items-center gap-8">
        <div className="flex items-center gap-3 cursor-pointer" onClick={() => onViewChange('chat')}>
          <div className="w-10 h-10 rounded-lg bg-white flex items-center justify-center shadow-sm border border-slate-100 overflow-hidden p-1">
             <img 
               src={logoUrl} 
               alt="Logo" 
               className="w-full h-full object-contain rounded" 
               referrerPolicy="no-referrer" 
             />
          </div>
          <span className="text-2xl font-black tracking-tighter text-slate-900">ATTIC<span className="text-blue-600">.</span></span>
        </div>

        <nav className="hidden md:flex items-center gap-6 text-sm font-semibold text-slate-500 uppercase tracking-wider">
          {[
            { id: 'chat', label: 'Public Rooms', category: 'public' },
            { id: 'chat', label: 'Private Rooms', category: 'private' },
            { id: 'chat', label: 'Personal', category: 'personal' },
            { id: 'contacts', label: 'Contacts' },
            { id: 'sessions', label: 'Sessions' }
          ].map((item) => (
            <button 
              key={item.label} 
              onClick={() => {
                onViewChange(item.id as View);
                if (item.category) onCategoryChange(item.category as ChatCategory);
              }}
              className={`pb-1 border-b-2 transition-colors ${
                activeView === item.id && (!item.category || chatCategory === item.category)
                ? 'text-blue-600 border-blue-600' 
                : 'text-slate-500 border-transparent hover:text-slate-900'
              }`}
            >
              {item.label}
            </button>
          ))}
        </nav>
      </div>

      <div className="flex items-center gap-4">
        <div 
          onClick={() => onViewChange('profile')}
          className={`flex items-center gap-2 text-sm font-bold px-3 py-1.5 rounded-full cursor-pointer transition-colors ${activeView === 'profile' ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-800 hover:bg-slate-200'}`}
        >
          <span className="w-2 h-2 bg-green-500 rounded-full"></span>
          Profile ▼
        </div>
        <button onClick={onSignOut} className="text-sm font-bold text-slate-400 hover:text-red-500 uppercase">
          Sign Out
        </button>
      </div>
    </header>
  );
};
