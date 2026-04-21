import { motion, AnimatePresence } from 'motion/react';
import { AuthMode } from '../../types';
import { Button, Input } from '../ui';
import { LOGO_URL } from '../../constants';

interface AuthProps {
  mode: AuthMode;
  onModeChange: (mode: AuthMode) => void;
  onLogin: () => void;
}

export const Auth = ({ mode, onModeChange, onLogin }: AuthProps) => {
  return (
    <div className="min-h-screen auth-gradient flex flex-col items-center justify-center p-4">
      <motion.div 
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        className="w-full max-w-md bg-white rounded-xl shadow-2xl overflow-hidden border border-slate-200"
      >
        <div className="p-8 border-b border-slate-100 flex items-center justify-center gap-3">
          <div className="w-14 h-14 bg-white rounded-xl flex items-center justify-center shadow-lg shadow-blue-500/10 overflow-hidden border border-slate-100 p-1">
            <img 
              src={LOGO_URL} 
              alt="Attic Logo" 
              className="w-full h-full object-contain rounded-lg" 
              referrerPolicy="no-referrer" 
            />
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-800 uppercase tracking-tighter">Attic</h1>
        </div>

        <div className="p-8">
          <AnimatePresence mode="wait">
            {mode === 'signin' && (
              <motion.div 
                key="signin"
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 20 }}
                className="flex flex-col gap-5"
              >
                <h2 className="text-lg font-semibold text-slate-700">Sign in to your account</h2>
                <Input label="Email" placeholder="you@company.com" />
                <Input label="Password" type="password" placeholder="••••••••" />
                
                <div className="flex items-center justify-between">
                  <label className="flex items-center gap-2 text-sm text-slate-600 cursor-pointer">
                    <input type="checkbox" className="w-4 h-4 rounded border-slate-300 text-blue-600 focus:ring-blue-500" />
                    Keep me signed in
                  </label>
                  <button 
                    onClick={() => onModeChange('forgot-password')}
                    className="text-sm text-blue-600 hover:underline font-medium"
                  >
                    Forgot my password
                  </button>
                </div>

                <Button onClick={onLogin} className="w-full py-3">Sign in</Button>
                
                <p className="text-sm text-slate-500 text-center mt-4">
                  Don't have an account? {' '}
                  <button onClick={() => onModeChange('register')} className="text-blue-600 font-semibold hover:underline">Register</button>
                </p>
              </motion.div>
            )}

            {mode === 'register' && (
              <motion.div 
                key="register"
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 20 }}
                className="flex flex-col gap-4"
              >
                <h2 className="text-lg font-semibold text-slate-700">Create an account</h2>
                <Input label="Email" placeholder="you@company.com" />
                <Input label="Username" placeholder="johndoe" />
                <Input label="Password" type="password" placeholder="••••••••" />
                <Input label="Confirm Password" type="password" placeholder="••••••••" />
                
                <Button onClick={onLogin} className="w-full py-3 mt-2">Create Account</Button>
                
                <p className="text-sm text-slate-500 text-center mt-2">
                  Already have an account? {' '}
                  <button onClick={() => onModeChange('signin')} className="text-blue-600 font-semibold hover:underline">Sign in</button>
                </p>
              </motion.div>
            )}

            {mode === 'forgot-password' && (
              <motion.div 
                key="forgot"
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 20 }}
                className="flex flex-col gap-5"
              >
                <h2 className="text-lg font-semibold text-slate-700">Reset your password</h2>
                <p className="text-sm text-slate-500 -mt-2">Enter your email to reset the password</p>
                <Input label="Email" placeholder="you@company.com" />
                
                <Button onClick={() => onModeChange('signin')} className="w-full py-3">Send reset link</Button>
                
                <button 
                  onClick={() => onModeChange('signin')}
                  className="text-sm text-slate-500 hover:text-slate-700 text-center font-medium"
                >
                  Back to Sign in
                </button>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </motion.div>
    </div>
  );
};
