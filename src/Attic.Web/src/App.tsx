import { Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { AuthGate } from './auth/AuthGate';
import { Login } from './auth/Login';
import { Register } from './auth/Register';
import { ChatShell } from './chat/ChatShell';

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route element={<AuthGate />}>
          <Route path="/" element={<ChatShell />} />
          <Route path="/chat/:channelId" element={<ChatShell />} />
          <Route path="/catalog" element={<ChatShell />} />
          <Route path="/invitations" element={<ChatShell />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}
