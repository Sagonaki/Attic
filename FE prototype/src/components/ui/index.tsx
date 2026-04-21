import { ReactNode, MouseEventHandler } from 'react';

interface ButtonProps {
  children: ReactNode;
  onClick?: MouseEventHandler<HTMLButtonElement>;
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger';
  className?: string;
  disabled?: boolean;
}

export const Button = ({ children, onClick, variant = 'primary', className = '', disabled }: ButtonProps) => {
  const base = "px-4 py-2 flex items-center justify-center gap-2 transition-all duration-200 uppercase tracking-widest text-xs font-black rounded-lg disabled:grayscale disabled:opacity-50";
  const variants = {
    primary: "bg-blue-600 text-white hover:bg-blue-700 shadow-lg shadow-blue-500/20",
    secondary: "bg-slate-900 text-white hover:bg-slate-800",
    ghost: "bg-slate-200 text-slate-900 hover:bg-slate-300",
    danger: "bg-white text-red-600 hover:bg-red-50 border border-red-200",
  };
  return (
    <button 
      onClick={onClick} 
      className={`${base} ${variants[variant]} ${className}`}
      disabled={disabled}
    >
      {children}
    </button>
  );
};

interface InputProps {
  label?: string;
  type?: string;
  value?: string;
  defaultValue?: string;
  onChange?: (e: any) => void;
  readOnly?: boolean;
  placeholder?: string;
  [key: string]: any;
}

export const Input = ({ label, type = "text", value, onChange, readOnly, ...props }: InputProps) => {
  const finalReadOnly = readOnly || (value !== undefined && onChange === undefined);
  return (
    <div className="flex flex-col gap-1 w-full">
      {label && <label className="text-[10px] font-black uppercase text-slate-400 tracking-[0.2em] mb-1">{label}</label>}
      <input 
        type={type}
        value={value}
        onChange={onChange}
        readOnly={finalReadOnly}
        className="w-full px-4 py-3 bg-white border border-slate-200 rounded-xl text-sm font-semibold focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all placeholder:text-slate-300"
        {...props}
      />
    </div>
  );
};
