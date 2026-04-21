import { Info, Users, Bell, Trash2, Shield, Lock, FileText, ChevronRight } from 'lucide-react';
import { Button } from '../ui';

export const RoomDetails = () => {
  return (
    <aside className="w-80 bg-slate-50 border-l border-slate-200 hidden xl:flex flex-col shrink-0">
      <div className="p-8 flex flex-col gap-8 h-full overflow-y-auto">
        {/* Info */}
        <div className="flex flex-col gap-4">
          <div className="flex items-center gap-3">
            <Info className="w-5 h-5 text-slate-400" />
            <h3 className="text-sm font-black text-slate-900 uppercase tracking-widest">Room Matrix</h3>
          </div>
          <div className="p-6 bg-white border-2 border-slate-900 rounded-3xl shadow-[8px_8px_0px_0px_rgba(15,23,42,1)]">
            <div className="text-3xl font-black text-slate-900 uppercase tracking-tighter mb-2">#engineering</div>
            <p className="text-xs font-bold text-slate-500 leading-relaxed italic mb-4">"The inner sanctum for technical architecture and system discussions."</p>
            <div className="flex items-center gap-4 text-[10px] font-black uppercase text-slate-400">
               <span className="flex items-center gap-1"><Users className="w-3 h-3" /> 42 MEMBERS</span>
               <span className="flex items-center gap-1">• PUBLIC</span>
            </div>
          </div>
        </div>

        {/* Media Snippets */}
        <div className="flex flex-col gap-4">
          <h3 className="text-xs font-black text-slate-400 uppercase tracking-[0.2em] px-1">Recent Files</h3>
          <div className="flex flex-col gap-2">
            {[
              { name: 'Architecture_Ref.pdf', size: '2.4mb', type: 'PDF' },
              { name: 'UI_Kit_Final.fig', size: '15.1mb', type: 'DESIGN' },
              { name: 'Global_Styles.css', size: '12kb', type: 'CODE' }
            ].map((file, idx) => (
              <div key={idx} className="flex items-center gap-3 p-3 bg-white rounded-xl border border-transparent hover:border-slate-200 hover:shadow-sm cursor-pointer transition-all">
                <div className="w-10 h-10 bg-slate-100 rounded-lg flex items-center justify-center text-slate-400 font-black text-[10px]">{file.type}</div>
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-black text-slate-900 truncate uppercase mt-0.5">{file.name}</div>
                  <div className="text-[10px] font-bold text-slate-400 uppercase tracking-widest">{file.size}</div>
                </div>
              </div>
            ))}
          </div>
          <button className="text-[10px] font-black uppercase text-blue-600 tracking-[0.2em] hover:underline mt-1">Explore all assets →</button>
        </div>

        {/* Members Quick List */}
        <div className="flex flex-col gap-4 mt-auto">
          <h3 className="text-xs font-black text-slate-400 uppercase tracking-[0.2em] px-1">Active Personnel</h3>
          <div className="flex flex-wrap gap-2">
            {['Alice', 'Bob', 'Charlie', 'Dave', 'Eve'].map((name, i) => (
              <div key={name} className={`w-8 h-8 rounded-lg flex items-center justify-center text-white font-black text-[10px] border-2 border-white shadow-sm ring-1 ring-slate-100 ${
                i === 0 ? 'bg-blue-500' : i === 1 ? 'bg-orange-400' : 'bg-slate-900'
              }`}>
                {name[0]}
              </div>
            ))}
            <div className="w-8 h-8 rounded-lg bg-slate-100 border border-slate-200 flex items-center justify-center text-slate-500 font-bold text-[10px] italic">+37</div>
          </div>
        </div>
      </div>
    </aside>
  );
};
