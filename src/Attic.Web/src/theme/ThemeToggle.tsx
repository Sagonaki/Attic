import { Moon, Sun, Monitor } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useTheme } from './ThemeProvider';

export function ThemeToggle() {
  const { theme, setTheme } = useTheme();
  const next = theme === 'light' ? 'dark' : theme === 'dark' ? 'system' : 'light';
  const Icon = theme === 'light' ? Sun : theme === 'dark' ? Moon : Monitor;
  const label =
    theme === 'light' ? 'Light mode (click for dark)'
    : theme === 'dark' ? 'Dark mode (click for system)'
    : 'System theme (click for light)';
  return (
    <Button variant="ghost" size="icon" onClick={() => setTheme(next)} aria-label={label} title={label}>
      <Icon className="h-4 w-4" />
    </Button>
  );
}
