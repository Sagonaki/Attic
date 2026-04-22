# Attic Phase 7 — Frontend Modernization (shadcn/ui + lucide-react) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lift the Attic SPA from "minimal Tailwind utilities + emoji" to a cohesive, accessible, modern UI by adopting shadcn/ui primitives, lucide-react icons, sonner toasts, and a proper design-token system with light/dark mode.

**Architecture:** shadcn/ui is copy-paste — we add each component's source directly under `src/components/ui/`. It's built on Radix primitives (proper focus management, keyboard nav, portal rendering for overlays) composed with `class-variance-authority` for variant styling, `clsx` + `tailwind-merge` (via a `cn()` helper) for conditional classes, and `tailwindcss-animate` for enter/exit transitions. Design tokens live as CSS custom properties in `index.css`, wired into Tailwind 4 via its `@theme` directive. A `<ThemeProvider>` toggles a `.dark` class on `<html>`; `<Toaster>` from `sonner` renders a dismissible toast stack. Existing feature components (`ChatShell`, `ChatWindow`, `Sidebar`, `CreateRoomModal`, `Contacts`, `RoomDetails`, `Sessions`, `DeleteAccountModal`, etc.) are refactored to consume the new primitives — no behavioral changes, just UI.

**Tech Stack additions:**
- `@radix-ui/react-dialog`, `@radix-ui/react-dropdown-menu`, `@radix-ui/react-tabs`, `@radix-ui/react-tooltip`, `@radix-ui/react-avatar`, `@radix-ui/react-separator`, `@radix-ui/react-scroll-area`, `@radix-ui/react-slot`
- `class-variance-authority`, `clsx`, `tailwind-merge`
- `lucide-react` (icons)
- `sonner` (toasts)
- `tailwindcss-animate`

No new backend dependencies, no server or database changes.

**Spec reference:** Deviates slightly from §10 of `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — the original spec assumed the React prototype's styling carried forward. Phase 7 replaces that with a consistent shadcn-based look while preserving all data-flow and hook contracts.

---

## Prerequisites

Do not regress any of these:

- **All 183 tests still green** after this phase. Backend is untouched.
- Hooks (`useChannelMessages`, `useSendMessage`, `useFriends`, `usePresence`, etc.) keep their exported signatures. Only the components that consume them change.
- Hub invocation paths from components go through `getOrCreateHubClient()` — unchanged.
- All current routes still resolve: `/`, `/login`, `/register`, `/chat/:channelId`, `/catalog`, `/invitations`, `/contacts`, `/settings/sessions`.
- Frontend `npm run lint && npm run build` exits 0 at the end of each commit (except intermediate ones explicitly noted).

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-7` (branched from merged `main` after Phase 6).
- `dotnet test` → 117 domain + 66 integration = 183 passing.
- `cd src/Attic.Web && npm run lint && npm run build` exits 0.
- Tailwind version: 4.1+ (CSS-first config via `@theme` in `index.css`).
- React 19, Vite 6.

---

## File structure additions

```
src/Attic.Web/src/
├── components/
│   └── ui/                                                    (new — shadcn/ui primitives)
│       ├── button.tsx
│       ├── input.tsx
│       ├── textarea.tsx
│       ├── dialog.tsx
│       ├── dropdown-menu.tsx
│       ├── tabs.tsx
│       ├── tooltip.tsx
│       ├── avatar.tsx
│       ├── badge.tsx
│       ├── separator.tsx
│       ├── scroll-area.tsx
│       ├── skeleton.tsx
│       └── sonner.tsx                                         (Toaster wrapper)
├── lib/
│   └── utils.ts                                               (new — cn() helper)
├── theme/
│   ├── ThemeProvider.tsx                                      (new)
│   └── ThemeToggle.tsx                                        (new)
├── App.tsx                                                    (modify — wrap with ThemeProvider + Toaster)
├── index.css                                                  (modify — design tokens + @theme)
├── auth/
│   ├── Login.tsx                                              (modify — Card, Input, Button)
│   ├── Register.tsx                                           (modify — Card, Input, Button)
│   ├── Sessions.tsx                                           (modify — Card layout + lucide icons)
│   └── DeleteAccountModal.tsx                                 (modify → Dialog)
├── chat/
│   ├── ChatShell.tsx                                          (modify — header DropdownMenu + ThemeToggle)
│   ├── Sidebar.tsx                                            (modify — Tabs + ScrollArea + icons)
│   ├── ChatInput.tsx                                          (modify — Button icons + upload chip polish)
│   ├── ChatWindow.tsx                                         (modify — Avatar, DropdownMenu actions, icons)
│   ├── CreateRoomModal.tsx                                    (modify → Dialog)
│   ├── SendFriendRequestModal.tsx                             (modify → Dialog + Command-style search)
│   ├── PublicCatalog.tsx                                      (modify — Card list + Skeleton loader)
│   ├── Contacts.tsx                                           (modify — Tabs + Avatar + DropdownMenu)
│   ├── InvitationsInbox.tsx                                   (modify — Card list)
│   ├── RoomDetails.tsx                                        (modify — Avatar + Badge roles + DropdownMenu)
│   ├── MessageActionsMenu.tsx                                 (modify → DropdownMenu)
│   └── ReplyPreview.tsx                                       (modify — lucide icons)
└── package.json                                               (modify — add deps)
```

Total: 13 new files (primitives + theme), ~15 modified files.

---

## Task ordering rationale

Four checkpoints:

- **Checkpoint 1 — Setup + primitives (Tasks 1-7):** install deps, design tokens, `cn()` helper, Button, Input, Textarea, Toaster, ThemeProvider.
- **Checkpoint 2 — Composite primitives (Tasks 8-13):** Dialog, DropdownMenu, Tabs, Tooltip, Avatar, Badge/Separator/ScrollArea/Skeleton.
- **Checkpoint 3 — Refactor (Tasks 14-23):** apply primitives to auth pages, sidebar, chat window, modals, catalog, contacts, room details, sessions.
- **Checkpoint 4 — Polish + smoke (Tasks 24-27):** toast integration on mutations, loading skeletons, theme toggle, build + lighthouse spot-check.

Each numbered task is one commit. Commit prefixes: `feat(web)`, `style(web)`, `refactor(web)`, `chore(web)`.

---

## Task 1: Install dependencies

**Files:**
- Modify: `src/Attic.Web/package.json` + `package-lock.json`

- [ ] **Step 1.1: Install**

```bash
cd src/Attic.Web
npm install --save \
  @radix-ui/react-dialog \
  @radix-ui/react-dropdown-menu \
  @radix-ui/react-tabs \
  @radix-ui/react-tooltip \
  @radix-ui/react-avatar \
  @radix-ui/react-separator \
  @radix-ui/react-scroll-area \
  @radix-ui/react-slot \
  class-variance-authority \
  clsx \
  tailwind-merge \
  lucide-react \
  sonner \
  tailwindcss-animate
cd -
```

- [ ] **Step 1.2: Commit**

```bash
git add src/Attic.Web/package.json src/Attic.Web/package-lock.json docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "chore(web): install shadcn/ui deps + lucide + sonner"
```

---

## Task 2: Design tokens in `index.css` + `cn()` helper

**Files:**
- Modify: `src/Attic.Web/src/index.css`
- Create: `src/Attic.Web/src/lib/utils.ts`

- [ ] **Step 2.1: `src/lib/utils.ts`**

```ts
import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 2.2: Replace `src/index.css`**

Tailwind 4 uses `@import "tailwindcss"` + `@theme` for CSS-first config. Retain any existing `@import` lines at the top and replace the body with:

```css
@import "tailwindcss";
@plugin "tailwindcss-animate";

@custom-variant dark (&:is(.dark *));

@theme {
  --color-background: hsl(0 0% 100%);
  --color-foreground: hsl(222.2 84% 4.9%);

  --color-card: hsl(0 0% 100%);
  --color-card-foreground: hsl(222.2 84% 4.9%);

  --color-popover: hsl(0 0% 100%);
  --color-popover-foreground: hsl(222.2 84% 4.9%);

  --color-primary: hsl(222.2 47.4% 11.2%);
  --color-primary-foreground: hsl(210 40% 98%);

  --color-secondary: hsl(210 40% 96.1%);
  --color-secondary-foreground: hsl(222.2 47.4% 11.2%);

  --color-muted: hsl(210 40% 96.1%);
  --color-muted-foreground: hsl(215.4 16.3% 46.9%);

  --color-accent: hsl(210 40% 96.1%);
  --color-accent-foreground: hsl(222.2 47.4% 11.2%);

  --color-destructive: hsl(0 84.2% 60.2%);
  --color-destructive-foreground: hsl(210 40% 98%);

  --color-border: hsl(214.3 31.8% 91.4%);
  --color-input: hsl(214.3 31.8% 91.4%);
  --color-ring: hsl(222.2 84% 4.9%);

  --radius: 0.5rem;
  --radius-sm: calc(var(--radius) - 4px);
  --radius-md: calc(var(--radius) - 2px);
  --radius-lg: var(--radius);
}

:root {
  color-scheme: light;
  background: var(--color-background);
  color: var(--color-foreground);
}

.dark {
  color-scheme: dark;
  --color-background: hsl(222.2 84% 4.9%);
  --color-foreground: hsl(210 40% 98%);
  --color-card: hsl(222.2 84% 4.9%);
  --color-card-foreground: hsl(210 40% 98%);
  --color-popover: hsl(222.2 84% 4.9%);
  --color-popover-foreground: hsl(210 40% 98%);
  --color-primary: hsl(210 40% 98%);
  --color-primary-foreground: hsl(222.2 47.4% 11.2%);
  --color-secondary: hsl(217.2 32.6% 17.5%);
  --color-secondary-foreground: hsl(210 40% 98%);
  --color-muted: hsl(217.2 32.6% 17.5%);
  --color-muted-foreground: hsl(215 20.2% 65.1%);
  --color-accent: hsl(217.2 32.6% 17.5%);
  --color-accent-foreground: hsl(210 40% 98%);
  --color-destructive: hsl(0 62.8% 30.6%);
  --color-destructive-foreground: hsl(210 40% 98%);
  --color-border: hsl(217.2 32.6% 17.5%);
  --color-input: hsl(217.2 32.6% 17.5%);
  --color-ring: hsl(212.7 26.8% 83.9%);
}

body {
  background: var(--color-background);
  color: var(--color-foreground);
  font-feature-settings: "rlig" 1, "calt" 1;
}

/* Scrollbar styling in dark mode */
*::-webkit-scrollbar { width: 8px; height: 8px; }
*::-webkit-scrollbar-track { background: transparent; }
*::-webkit-scrollbar-thumb { background: var(--color-border); border-radius: 4px; }
*::-webkit-scrollbar-thumb:hover { background: var(--color-muted-foreground); }
```

This declares semantic color tokens (e.g. `bg-primary`, `text-muted-foreground`, `border-border`) that shadcn components reference. Tailwind 4's `@theme` makes `--color-x` generate both a `bg-x` / `text-x` / `border-x` utility and the underlying CSS var.

- [ ] **Step 2.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/index.css src/Attic.Web/src/lib/utils.ts docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): design tokens + cn() helper (shadcn foundation)"
```

---

## Task 3: `Button` component

**Files:**
- Create: `src/Attic.Web/src/components/ui/button.tsx`

- [ ] **Step 3.1: Write `button.tsx`**

```tsx
import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground hover:bg-primary/90',
        destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
        outline: 'border border-input bg-background hover:bg-accent hover:text-accent-foreground',
        secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/80',
        ghost: 'hover:bg-accent hover:text-accent-foreground',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-9 px-4 py-2',
        sm: 'h-8 rounded-md px-3 text-xs',
        lg: 'h-10 rounded-md px-8',
        icon: 'h-9 w-9',
      },
    },
    defaultVariants: { variant: 'default', size: 'default' },
  }
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : 'button';
    return <Comp className={cn(buttonVariants({ variant, size }), className)} ref={ref} {...props} />;
  }
);
Button.displayName = 'Button';

export { buttonVariants };
```

- [ ] **Step 3.2: Configure Vite path alias `@` → `src/`**

Open `src/Attic.Web/vite.config.ts` (or `.js`). Add to the `defineConfig` object:

```ts
import path from 'node:path';
// inside defineConfig:
resolve: {
  alias: {
    '@': path.resolve(__dirname, './src'),
  },
},
```

And to `tsconfig.json` (or `tsconfig.app.json`):

```json
"compilerOptions": {
  "baseUrl": ".",
  "paths": { "@/*": ["src/*"] }
}
```

Verify both files exist and adapt to their existing structure — insert paths next to existing compiler options.

- [ ] **Step 3.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui/button.tsx src/Attic.Web/vite.config.ts src/Attic.Web/tsconfig.json src/Attic.Web/tsconfig.app.json docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): Button primitive + @/ path alias"
```

Stage only the tsconfig files that actually exist.

---

## Task 4: `Input` + `Textarea` primitives

**Files:**
- Create: `src/Attic.Web/src/components/ui/input.tsx`
- Create: `src/Attic.Web/src/components/ui/textarea.tsx`

- [ ] **Step 4.1: `input.tsx`**

```tsx
import * as React from 'react';
import { cn } from '@/lib/utils';

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, type, ...props }, ref) => (
    <input
      type={type}
      className={cn(
        'flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50',
        className
      )}
      ref={ref}
      {...props}
    />
  )
);
Input.displayName = 'Input';
```

- [ ] **Step 4.2: `textarea.tsx`**

```tsx
import * as React from 'react';
import { cn } from '@/lib/utils';

export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...props }, ref) => (
    <textarea
      className={cn(
        'flex min-h-[60px] w-full rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50',
        className
      )}
      ref={ref}
      {...props}
    />
  )
);
Textarea.displayName = 'Textarea';
```

- [ ] **Step 4.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui/input.tsx src/Attic.Web/src/components/ui/textarea.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): Input + Textarea primitives"
```

---

## Task 5: `Skeleton` + `Badge` + `Separator` + `ScrollArea` primitives

**Files:**
- Create: `src/Attic.Web/src/components/ui/skeleton.tsx`
- Create: `src/Attic.Web/src/components/ui/badge.tsx`
- Create: `src/Attic.Web/src/components/ui/separator.tsx`
- Create: `src/Attic.Web/src/components/ui/scroll-area.tsx`

- [ ] **Step 5.1: `skeleton.tsx`**

```tsx
import { cn } from '@/lib/utils';

export function Skeleton({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('animate-pulse rounded-md bg-muted', className)} {...props} />;
}
```

- [ ] **Step 5.2: `badge.tsx`**

```tsx
import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

const badgeVariants = cva(
  'inline-flex items-center rounded-md border px-2.5 py-0.5 text-xs font-semibold transition-colors',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground',
        secondary: 'border-transparent bg-secondary text-secondary-foreground',
        destructive: 'border-transparent bg-destructive text-destructive-foreground',
        outline: 'text-foreground',
      },
    },
    defaultVariants: { variant: 'default' },
  }
);

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />;
}
```

- [ ] **Step 5.3: `separator.tsx`**

```tsx
import * as React from 'react';
import * as SeparatorPrimitive from '@radix-ui/react-separator';
import { cn } from '@/lib/utils';

export const Separator = React.forwardRef<
  React.ElementRef<typeof SeparatorPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof SeparatorPrimitive.Root>
>(({ className, orientation = 'horizontal', decorative = true, ...props }, ref) => (
  <SeparatorPrimitive.Root
    ref={ref}
    decorative={decorative}
    orientation={orientation}
    className={cn('shrink-0 bg-border', orientation === 'horizontal' ? 'h-[1px] w-full' : 'h-full w-[1px]', className)}
    {...props}
  />
));
Separator.displayName = SeparatorPrimitive.Root.displayName;
```

- [ ] **Step 5.4: `scroll-area.tsx`**

```tsx
import * as React from 'react';
import * as ScrollAreaPrimitive from '@radix-ui/react-scroll-area';
import { cn } from '@/lib/utils';

export const ScrollArea = React.forwardRef<
  React.ElementRef<typeof ScrollAreaPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof ScrollAreaPrimitive.Root>
>(({ className, children, ...props }, ref) => (
  <ScrollAreaPrimitive.Root ref={ref} className={cn('relative overflow-hidden', className)} {...props}>
    <ScrollAreaPrimitive.Viewport className="h-full w-full rounded-[inherit]">
      {children}
    </ScrollAreaPrimitive.Viewport>
    <ScrollBar />
    <ScrollAreaPrimitive.Corner />
  </ScrollAreaPrimitive.Root>
));
ScrollArea.displayName = ScrollAreaPrimitive.Root.displayName;

export const ScrollBar = React.forwardRef<
  React.ElementRef<typeof ScrollAreaPrimitive.ScrollAreaScrollbar>,
  React.ComponentPropsWithoutRef<typeof ScrollAreaPrimitive.ScrollAreaScrollbar>
>(({ className, orientation = 'vertical', ...props }, ref) => (
  <ScrollAreaPrimitive.ScrollAreaScrollbar
    ref={ref}
    orientation={orientation}
    className={cn(
      'flex touch-none select-none transition-colors',
      orientation === 'vertical' && 'h-full w-2.5 border-l border-l-transparent p-[1px]',
      orientation === 'horizontal' && 'h-2.5 flex-col border-t border-t-transparent p-[1px]',
      className
    )}
    {...props}
  >
    <ScrollAreaPrimitive.ScrollAreaThumb className="relative flex-1 rounded-full bg-border" />
  </ScrollAreaPrimitive.ScrollAreaScrollbar>
));
ScrollBar.displayName = ScrollAreaPrimitive.ScrollAreaScrollbar.displayName;
```

- [ ] **Step 5.5: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): Skeleton + Badge + Separator + ScrollArea primitives"
```

---

## Task 6: `ThemeProvider` + `ThemeToggle`

**Files:**
- Create: `src/Attic.Web/src/theme/ThemeProvider.tsx`
- Create: `src/Attic.Web/src/theme/ThemeToggle.tsx`

- [ ] **Step 6.1: `ThemeProvider.tsx`**

```tsx
import * as React from 'react';

type Theme = 'light' | 'dark' | 'system';

interface ThemeContextValue {
  theme: Theme;
  setTheme: (t: Theme) => void;
  resolvedTheme: 'light' | 'dark';
}

const ThemeContext = React.createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = React.useState<Theme>(() => {
    const stored = typeof window !== 'undefined' ? window.localStorage.getItem('attic.theme') : null;
    return (stored as Theme) ?? 'system';
  });

  const resolvedTheme = React.useMemo<'light' | 'dark'>(() => {
    if (theme === 'system') {
      return typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches
        ? 'dark'
        : 'light';
    }
    return theme;
  }, [theme]);

  React.useEffect(() => {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(resolvedTheme);
  }, [resolvedTheme]);

  const setTheme = React.useCallback((t: Theme) => {
    window.localStorage.setItem('attic.theme', t);
    setThemeState(t);
  }, []);

  const value = React.useMemo(() => ({ theme, setTheme, resolvedTheme }), [theme, setTheme, resolvedTheme]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const ctx = React.useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used inside <ThemeProvider>');
  return ctx;
}
```

- [ ] **Step 6.2: `ThemeToggle.tsx`**

```tsx
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
```

- [ ] **Step 6.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/theme docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): ThemeProvider with light/dark/system + ThemeToggle"
```

---

## Task 7: Wrap `App.tsx` with `ThemeProvider` + `Toaster`

**Files:**
- Create: `src/Attic.Web/src/components/ui/sonner.tsx`
- Modify: `src/Attic.Web/src/App.tsx`

- [ ] **Step 7.1: `sonner.tsx`**

```tsx
import { Toaster as Sonner } from 'sonner';
import { useTheme } from '@/theme/ThemeProvider';

export function Toaster(props: React.ComponentProps<typeof Sonner>) {
  const { resolvedTheme } = useTheme();
  return (
    <Sonner
      theme={resolvedTheme}
      className="toaster group"
      toastOptions={{
        classNames: {
          toast: 'group toast group-[.toaster]:bg-card group-[.toaster]:text-card-foreground group-[.toaster]:border-border group-[.toaster]:shadow-lg',
          description: 'group-[.toast]:text-muted-foreground',
          actionButton: 'group-[.toast]:bg-primary group-[.toast]:text-primary-foreground',
          cancelButton: 'group-[.toast]:bg-muted group-[.toast]:text-muted-foreground',
        },
      }}
      {...props}
    />
  );
}
```

- [ ] **Step 7.2: Wrap `App.tsx`**

Replace the outermost JSX wrapper. Existing App.tsx is:

```tsx
export default function App() {
  return (
    <AuthProvider>
      <Routes>
        ...
      </Routes>
    </AuthProvider>
  );
}
```

Change to:

```tsx
import { ThemeProvider } from './theme/ThemeProvider';
import { Toaster } from './components/ui/sonner';

export default function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <Routes>
          ...existing routes...
        </Routes>
        <Toaster />
      </AuthProvider>
    </ThemeProvider>
  );
}
```

- [ ] **Step 7.3: Build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/components/ui/sonner.tsx src/Attic.Web/src/App.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): mount ThemeProvider + Toaster at app root"
```

Expected: 0 lint errors, 0 build errors.

---

## Task 8: `Dialog` primitive

**Files:**
- Create: `src/Attic.Web/src/components/ui/dialog.tsx`

- [ ] **Step 8.1: Write the file**

```tsx
import * as React from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogPortal = DialogPrimitive.Portal;
export const DialogClose = DialogPrimitive.Close;

export const DialogOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      'fixed inset-0 z-50 bg-black/80 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
      className
    )}
    {...props}
  />
));
DialogOverlay.displayName = DialogPrimitive.Overlay.displayName;

export const DialogContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content>
>(({ className, children, ...props }, ref) => (
  <DialogPortal>
    <DialogOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        'fixed left-[50%] top-[50%] z-50 grid w-full max-w-lg translate-x-[-50%] translate-y-[-50%] gap-4 border bg-background p-6 shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] sm:rounded-lg',
        className
      )}
      {...props}
    >
      {children}
      <DialogPrimitive.Close className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:pointer-events-none">
        <X className="h-4 w-4" />
        <span className="sr-only">Close</span>
      </DialogPrimitive.Close>
    </DialogPrimitive.Content>
  </DialogPortal>
));
DialogContent.displayName = DialogPrimitive.Content.displayName;

export function DialogHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col space-y-1.5 text-center sm:text-left', className)} {...props} />;
}

export function DialogFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2', className)} {...props} />;
}

export const DialogTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title ref={ref} className={cn('text-lg font-semibold leading-none tracking-tight', className)} {...props} />
));
DialogTitle.displayName = DialogPrimitive.Title.displayName;

export const DialogDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description ref={ref} className={cn('text-sm text-muted-foreground', className)} {...props} />
));
DialogDescription.displayName = DialogPrimitive.Description.displayName;
```

- [ ] **Step 8.2: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui/dialog.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): Dialog primitive"
```

---

## Task 9: `DropdownMenu` primitive

**Files:**
- Create: `src/Attic.Web/src/components/ui/dropdown-menu.tsx`

- [ ] **Step 9.1: Write**

```tsx
import * as React from 'react';
import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu';
import { Check, ChevronRight, Circle } from 'lucide-react';
import { cn } from '@/lib/utils';

export const DropdownMenu = DropdownMenuPrimitive.Root;
export const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger;
export const DropdownMenuGroup = DropdownMenuPrimitive.Group;
export const DropdownMenuPortal = DropdownMenuPrimitive.Portal;
export const DropdownMenuSub = DropdownMenuPrimitive.Sub;
export const DropdownMenuRadioGroup = DropdownMenuPrimitive.RadioGroup;

export const DropdownMenuContent = React.forwardRef<
  React.ElementRef<typeof DropdownMenuPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Content>
>(({ className, sideOffset = 4, ...props }, ref) => (
  <DropdownMenuPrimitive.Portal>
    <DropdownMenuPrimitive.Content
      ref={ref}
      sideOffset={sideOffset}
      className={cn(
        'z-50 min-w-[8rem] overflow-hidden rounded-md border bg-popover p-1 text-popover-foreground shadow-md',
        'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
        className
      )}
      {...props}
    />
  </DropdownMenuPrimitive.Portal>
));
DropdownMenuContent.displayName = DropdownMenuPrimitive.Content.displayName;

export const DropdownMenuItem = React.forwardRef<
  React.ElementRef<typeof DropdownMenuPrimitive.Item>,
  React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Item> & { inset?: boolean }
>(({ className, inset, ...props }, ref) => (
  <DropdownMenuPrimitive.Item
    ref={ref}
    className={cn(
      'relative flex cursor-default select-none items-center gap-2 rounded-sm px-2 py-1.5 text-sm outline-none transition-colors focus:bg-accent focus:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
      inset && 'pl-8',
      className
    )}
    {...props}
  />
));
DropdownMenuItem.displayName = DropdownMenuPrimitive.Item.displayName;

export const DropdownMenuLabel = React.forwardRef<
  React.ElementRef<typeof DropdownMenuPrimitive.Label>,
  React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Label>
>(({ className, ...props }, ref) => (
  <DropdownMenuPrimitive.Label ref={ref} className={cn('px-2 py-1.5 text-sm font-semibold', className)} {...props} />
));
DropdownMenuLabel.displayName = DropdownMenuPrimitive.Label.displayName;

export const DropdownMenuSeparator = React.forwardRef<
  React.ElementRef<typeof DropdownMenuPrimitive.Separator>,
  React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Separator>
>(({ className, ...props }, ref) => (
  <DropdownMenuPrimitive.Separator ref={ref} className={cn('-mx-1 my-1 h-px bg-muted', className)} {...props} />
));
DropdownMenuSeparator.displayName = DropdownMenuPrimitive.Separator.displayName;

export function DropdownMenuShortcut({ className, ...props }: React.HTMLAttributes<HTMLSpanElement>) {
  return <span className={cn('ml-auto text-xs tracking-widest opacity-60', className)} {...props} />;
}
```

- [ ] **Step 9.2: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui/dropdown-menu.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): DropdownMenu primitive"
```

---

## Task 10: `Tabs` + `Tooltip` + `Avatar`

**Files:**
- Create: `src/Attic.Web/src/components/ui/tabs.tsx`
- Create: `src/Attic.Web/src/components/ui/tooltip.tsx`
- Create: `src/Attic.Web/src/components/ui/avatar.tsx`

- [ ] **Step 10.1: `tabs.tsx`**

```tsx
import * as React from 'react';
import * as TabsPrimitive from '@radix-ui/react-tabs';
import { cn } from '@/lib/utils';

export const Tabs = TabsPrimitive.Root;

export const TabsList = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.List>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.List>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.List
    ref={ref}
    className={cn('inline-flex h-9 items-center justify-center rounded-lg bg-muted p-1 text-muted-foreground', className)}
    {...props}
  />
));
TabsList.displayName = TabsPrimitive.List.displayName;

export const TabsTrigger = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Trigger>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Trigger
    ref={ref}
    className={cn(
      'inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-sm font-medium transition-all focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50 data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow',
      className
    )}
    {...props}
  />
));
TabsTrigger.displayName = TabsPrimitive.Trigger.displayName;

export const TabsContent = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Content>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Content
    ref={ref}
    className={cn('mt-2 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring', className)}
    {...props}
  />
));
TabsContent.displayName = TabsPrimitive.Content.displayName;
```

- [ ] **Step 10.2: `tooltip.tsx`**

```tsx
import * as React from 'react';
import * as TooltipPrimitive from '@radix-ui/react-tooltip';
import { cn } from '@/lib/utils';

export const TooltipProvider = TooltipPrimitive.Provider;
export const Tooltip = TooltipPrimitive.Root;
export const TooltipTrigger = TooltipPrimitive.Trigger;

export const TooltipContent = React.forwardRef<
  React.ElementRef<typeof TooltipPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof TooltipPrimitive.Content>
>(({ className, sideOffset = 4, ...props }, ref) => (
  <TooltipPrimitive.Content
    ref={ref}
    sideOffset={sideOffset}
    className={cn(
      'z-50 overflow-hidden rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground animate-in fade-in-0 zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95',
      className
    )}
    {...props}
  />
));
TooltipContent.displayName = TooltipPrimitive.Content.displayName;
```

- [ ] **Step 10.3: `avatar.tsx`**

```tsx
import * as React from 'react';
import * as AvatarPrimitive from '@radix-ui/react-avatar';
import { cn } from '@/lib/utils';

export const Avatar = React.forwardRef<
  React.ElementRef<typeof AvatarPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof AvatarPrimitive.Root>
>(({ className, ...props }, ref) => (
  <AvatarPrimitive.Root
    ref={ref}
    className={cn('relative flex h-8 w-8 shrink-0 overflow-hidden rounded-full', className)}
    {...props}
  />
));
Avatar.displayName = AvatarPrimitive.Root.displayName;

export const AvatarImage = React.forwardRef<
  React.ElementRef<typeof AvatarPrimitive.Image>,
  React.ComponentPropsWithoutRef<typeof AvatarPrimitive.Image>
>(({ className, ...props }, ref) => (
  <AvatarPrimitive.Image ref={ref} className={cn('aspect-square h-full w-full', className)} {...props} />
));
AvatarImage.displayName = AvatarPrimitive.Image.displayName;

export const AvatarFallback = React.forwardRef<
  React.ElementRef<typeof AvatarPrimitive.Fallback>,
  React.ComponentPropsWithoutRef<typeof AvatarPrimitive.Fallback>
>(({ className, ...props }, ref) => (
  <AvatarPrimitive.Fallback
    ref={ref}
    className={cn('flex h-full w-full items-center justify-center rounded-full bg-muted text-muted-foreground text-xs font-medium', className)}
    {...props}
  />
));
AvatarFallback.displayName = AvatarPrimitive.Fallback.displayName;

/**
 * Deterministic "initials + color" avatar. Derives a stable hue from the username
 * so the same user always gets the same color.
 */
export function UserAvatar({ username, className }: { username: string | undefined; className?: string }) {
  const initials = (username ?? '?').slice(0, 2).toUpperCase();
  const hue = React.useMemo(() => {
    const s = username ?? '';
    let h = 0;
    for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
    return h % 360;
  }, [username]);
  return (
    <Avatar className={className}>
      <AvatarFallback style={{ background: `hsl(${hue} 60% 85%)`, color: `hsl(${hue} 70% 25%)` }}>
        {initials}
      </AvatarFallback>
    </Avatar>
  );
}
```

- [ ] **Step 10.4: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/components/ui/tabs.tsx src/Attic.Web/src/components/ui/tooltip.tsx src/Attic.Web/src/components/ui/avatar.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): Tabs + Tooltip + Avatar (with deterministic UserAvatar)"
```

---

## Task 11: Checkpoint 1+2 marker (setup done)

- [ ] **Step 11.1: Final setup verification**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 0 errors. Build size may have grown slightly — that's expected.

- [ ] **Step 11.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 7 Checkpoint 1+2 (primitives ready)"
```

---

## Task 12: Refactor `Login` + `Register` auth pages

**Files:**
- Modify: `src/Attic.Web/src/auth/Login.tsx`
- Modify: `src/Attic.Web/src/auth/Register.tsx`

Wrap each form in a centered card, replace raw `<input>` / `<button>` with `<Input>` / `<Button>`, add a lucide icon header (LogIn / UserPlus).

- [ ] **Step 12.1: `Login.tsx` — replace with**

```tsx
import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { LogIn } from 'lucide-react';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export function Login() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const user = await api.post<{ id: string; username: string; email: string }>('/api/auth/login',
        { email, password });
      setUser({ id: user.id, username: user.username, email: user.email });
      navigate('/', { replace: true });
    } catch (e) {
      setError((e as Error).message ?? 'Login failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-sm border bg-card text-card-foreground rounded-lg shadow-sm p-6 space-y-4">
        <div className="flex items-center gap-2">
          <LogIn className="h-5 w-5" />
          <h1 className="text-xl font-semibold">Sign in</h1>
        </div>
        <form onSubmit={onSubmit} className="space-y-3">
          <Input type="email" placeholder="Email" autoComplete="email" required
                 value={email} onChange={e => setEmail(e.target.value)} />
          <Input type="password" placeholder="Password" autoComplete="current-password" required
                 value={password} onChange={e => setPassword(e.target.value)} />
          {error && <div className="text-sm text-destructive">{error}</div>}
          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
        <div className="text-sm text-muted-foreground text-center">
          No account? <Link to="/register" className="underline underline-offset-4">Register</Link>
        </div>
      </div>
    </div>
  );
}
```

If Phase 1's `Login.tsx` has different response handling or field names, adapt — the goal is preserving behavior while swapping visual scaffolding.

- [ ] **Step 12.2: `Register.tsx` — same pattern**

Apply the same layout to `Register.tsx`. Fields: email, username, password. Icon: `UserPlus`. Bottom link: "Already have an account? Sign in".

- [ ] **Step 12.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/auth/Login.tsx src/Attic.Web/src/auth/Register.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): Login + Register as Card + Input + Button + icon"
```

---

## Task 13: `ChatShell` — header refresh with `DropdownMenu` + `ThemeToggle`

**Files:**
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`

- [ ] **Step 13.1: Extract header as internal component**

Replace the existing `<header>` block at the top of `ChatShell` with a user DropdownMenu + theme toggle:

```tsx
import { Moon, Sun, Monitor, User, LogOut, Trash2 } from 'lucide-react';
// ...plus existing imports
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
  DropdownMenuSeparator, DropdownMenuLabel,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { UserAvatar } from '@/components/ui/avatar';
import { ThemeToggle } from '../theme/ThemeToggle';
```

Replace the `<header className="flex items-center justify-between px-4 py-2 border-b bg-white">` block with:

```tsx
      <header className="flex items-center justify-between px-4 py-2 border-b bg-card text-card-foreground">
        <div className="font-semibold">Attic</div>
        <div className="flex items-center gap-1">
          <ThemeToggle />
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="gap-2">
                <UserAvatar username={user?.username} className="h-6 w-6" />
                <span className="text-sm">{user?.username}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>Account</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={logout}>
                <LogOut className="h-4 w-4" /> Sign out
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => setDeleteOpen(true)} className="text-destructive focus:text-destructive">
                <Trash2 className="h-4 w-4" /> Delete account
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>
```

- [ ] **Step 13.2: Update the main container background**

The outer `<div className="h-screen flex flex-col">` can stay; inside, replace `bg-slate-50` / `bg-white` with `bg-background` / `bg-card` where applicable.

- [ ] **Step 13.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/ChatShell.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): ChatShell header with UserAvatar + DropdownMenu + ThemeToggle"
```

---

## Task 14: `Sidebar` — Tabs primitive + ScrollArea + lucide icons

**Files:**
- Modify: `src/Attic.Web/src/chat/Sidebar.tsx`

Replace the manual tab buttons with the shadcn `Tabs` primitive, wrap the channel list in `ScrollArea`, swap emoji / text links for lucide icons.

- [ ] **Step 14.1: Replace `Sidebar.tsx` body**

The full contents:

```tsx
import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Globe, Lock, MessageSquare, Plus, BookOpen, Mail, Users, Settings } from 'lucide-react';
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
        <Button asChild variant="outline" size="sm" className="flex-1">
          <Link to="/catalog"><BookOpen className="h-3.5 w-3.5" />Catalog</Link>
        </Button>
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
                  <span className="truncate">{c.name ?? 'Personal chat'}</span>
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
      <div className="p-2 grid grid-cols-3 gap-1">
        <Button asChild variant="ghost" size="sm"><Link to="/contacts"><Users className="h-3.5 w-3.5" />Contacts</Link></Button>
        <Button asChild variant="ghost" size="sm"><Link to="/invitations"><Mail className="h-3.5 w-3.5" />Invites</Link></Button>
        <Button asChild variant="ghost" size="sm"><Link to="/settings/sessions"><Settings className="h-3.5 w-3.5" />Settings</Link></Button>
      </div>
    </aside>
  );
}
```

- [ ] **Step 14.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/Sidebar.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): Sidebar with Tabs + ScrollArea + lucide icons + unread Badge"
```

---

## Task 15: `MessageActionsMenu` → `DropdownMenu` + `ChatWindow` polish

**Files:**
- Modify: `src/Attic.Web/src/chat/MessageActionsMenu.tsx`
- Modify: `src/Attic.Web/src/chat/ChatWindow.tsx`

- [ ] **Step 15.1: Rewrite `MessageActionsMenu.tsx`**

```tsx
import { MoreHorizontal, Reply, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export function MessageActionsMenu({
  isOwn, isAdmin, onEdit, onReply, onDelete,
}: {
  isOwn: boolean;
  isAdmin: boolean;
  onEdit: () => void;
  onReply: () => void;
  onDelete: () => void;
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity">
          <MoreHorizontal className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={onReply}><Reply className="h-4 w-4" />Reply</DropdownMenuItem>
        {isOwn && <DropdownMenuItem onClick={onEdit}><Pencil className="h-4 w-4" />Edit</DropdownMenuItem>}
        {(isOwn || isAdmin) && (
          <DropdownMenuItem onClick={onDelete} className="text-destructive focus:text-destructive">
            <Trash2 className="h-4 w-4" />Delete
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 15.2: Update `ChatWindow.tsx`**

Replace the existing action-button pattern (the inline `<button>` that triggered `setMenuMsgId`) — the DropdownMenu now controls its own open state. Delete the `menuMsgId` state and its setter; the button inside `MessageActionsMenu` is always rendered but hidden via CSS until hover.

Update the message row:

```tsx
import { UserAvatar } from '@/components/ui/avatar';
import { MessageActionsMenu } from './MessageActionsMenu';

// ...inside the map:
<div key={m.id} className="group flex gap-2 hover:bg-accent/40 rounded-md px-2 py-1 transition-colors">
  <UserAvatar username={m.senderUsername} className="h-8 w-8 mt-0.5" />
  <div className="flex-1 min-w-0">
    <div className="flex items-center justify-between gap-2">
      <div className="text-xs text-muted-foreground">
        <span className="font-medium text-foreground">{m.senderUsername}</span>
        <span className="ml-2">{new Date(m.createdAt).toLocaleTimeString()}</span>
        {m.updatedAt && <span className="ml-2">(edited)</span>}
        {m.id < 0 && <span className="ml-2 italic">sending…</span>}
      </div>
      {m.id > 0 && (
        <MessageActionsMenu
          isOwn={m.senderId === user.id}
          isAdmin={false}
          onEdit={() => { setEditingId(m.id); setEditDraft(m.content); }}
          onReply={() => setReplyTo({ messageId: m.id, snippet: m.content.slice(0, 80) })}
          onDelete={() => void del(m.id)}
        />
      )}
    </div>
    {m.replyToId && byId.get(m.replyToId) && (
      <div className="text-xs text-muted-foreground border-l-2 border-muted-foreground/30 pl-2 my-1">
        <span className="font-medium">{byId.get(m.replyToId)!.senderUsername}: </span>
        {byId.get(m.replyToId)!.content.slice(0, 80)}
      </div>
    )}
    {editingId === m.id ? (
      <div className="flex gap-2 items-center">
        <Input value={editDraft} onChange={e => setEditDraft(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') void saveEdit(); if (e.key === 'Escape') setEditingId(null); }}
               autoFocus />
        <Button size="sm" onClick={saveEdit}>Save</Button>
        <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>Cancel</Button>
      </div>
    ) : (
      <>
        <div className="whitespace-pre-wrap break-words text-sm">{m.content}</div>
        {m.attachments?.map(a => <AttachmentPreview key={a.id} attachment={a} />)}
      </>
    )}
  </div>
</div>
```

Update imports:
```tsx
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
```

Remove the `menuMsgId` state and its setter.

Update the outer container's background classes: `bg-slate-50` → `bg-background`, `bg-white` → remove (messages are transparent, hover shows accent).

- [ ] **Step 15.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/MessageActionsMenu.tsx src/Attic.Web/src/chat/ChatWindow.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): ChatWindow uses Avatar + DropdownMenu actions + Input edit"
```

---

## Task 16: `ChatInput` polish

**Files:**
- Modify: `src/Attic.Web/src/chat/ChatInput.tsx`
- Modify: `src/Attic.Web/src/chat/ReplyPreview.tsx`

Replace raw `<button>` / `<textarea>` with primitives, use lucide icons, style upload chips.

- [ ] **Step 16.1: `ReplyPreview.tsx`**

```tsx
import { X, Reply } from 'lucide-react';
import { Button } from '@/components/ui/button';

export function ReplyPreview({ replySnippet, onCancel }: { replySnippet: string; onCancel: () => void }) {
  return (
    <div className="flex items-center justify-between px-3 py-1.5 bg-muted text-xs text-muted-foreground border-t border-b">
      <span className="flex items-center gap-2">
        <Reply className="h-3 w-3" />
        Replying to: <em className="text-foreground/80">{replySnippet}</em>
      </span>
      <Button variant="ghost" size="icon" className="h-5 w-5" onClick={onCancel}>
        <X className="h-3 w-3" />
      </Button>
    </div>
  );
}
```

- [ ] **Step 16.2: `ChatInput.tsx`**

Replace the render output (keep the handlers and state):

```tsx
import { useRef, useState } from 'react';
import { Paperclip, Send, X, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { useUploadAttachments } from './useUploadAttachments';
import { ReplyPreview } from './ReplyPreview';

type OnSend = (
  content: string,
  opts?: { replyToId?: number | null; attachmentIds?: string[] }
) => void | Promise<void>;

export interface ChatInputProps {
  onSend: OnSend;
  replyTo?: { messageId: number; snippet: string } | null;
  onCancelReply?: () => void;
}

export function ChatInput({ onSend, replyTo, onCancelReply }: ChatInputProps) {
  const [content, setContent] = useState('');
  const { pending, upload, clear, removeOne } = useUploadAttachments();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const readyAttachments = pending.filter(p => p.status === 'done' && p.attachment).map(p => p.attachment!.id);
  const isBusy = pending.some(p => p.status === 'uploading');

  async function submit() {
    if (isBusy) return;
    if (!content.trim() && readyAttachments.length === 0) return;
    await onSend(content.trim(), { replyToId: replyTo?.messageId ?? null, attachmentIds: readyAttachments });
    setContent('');
    clear();
    onCancelReply?.();
  }

  function onPaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const files = Array.from(e.clipboardData?.files ?? []);
    if (files.length > 0) { e.preventDefault(); void upload(files); }
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    const files = Array.from(e.dataTransfer?.files ?? []);
    if (files.length > 0) void upload(files);
  }

  return (
    <div onDragOver={e => e.preventDefault()} onDrop={onDrop}>
      {replyTo && <ReplyPreview replySnippet={replyTo.snippet} onCancel={() => onCancelReply?.()} />}
      {pending.length > 0 && (
        <div className="flex flex-wrap gap-2 p-2 bg-muted/50 border-t">
          {pending.map(p => (
            <Badge key={p.id} variant="secondary" className="gap-1 pr-1">
              <FileText className="h-3 w-3" />
              <span className="max-w-[12rem] truncate">{p.file.name}</span>
              {p.status === 'uploading' && <span className="text-muted-foreground">…</span>}
              {p.status === 'error' && <span className="text-destructive">!</span>}
              <button onClick={() => removeOne(p.id)} className="ml-1 rounded hover:bg-background/50 p-0.5">
                <X className="h-3 w-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}
      <div className="flex items-end gap-2 p-3 border-t bg-card">
        <input ref={fileInputRef} type="file" multiple className="hidden"
               onChange={e => { if (e.target.files) { void upload(Array.from(e.target.files)); e.target.value = ''; } }} />
        <Button variant="ghost" size="icon" onClick={() => fileInputRef.current?.click()} aria-label="Attach file">
          <Paperclip className="h-4 w-4" />
        </Button>
        <Textarea
          className="flex-1 min-h-[40px] max-h-40 resize-none"
          rows={1}
          placeholder="Type a message…"
          value={content}
          onChange={e => setContent(e.target.value)}
          onPaste={onPaste}
          onKeyDown={e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(); }
          }}
        />
        <Button onClick={submit} disabled={isBusy} aria-label="Send message">
          <Send className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
```

- [ ] **Step 16.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/ChatInput.tsx src/Attic.Web/src/chat/ReplyPreview.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): ChatInput + ReplyPreview with icons + Textarea + Badge chips"
```

---

## Task 17: Modals → `Dialog`

**Files:**
- Modify: `src/Attic.Web/src/chat/CreateRoomModal.tsx`
- Modify: `src/Attic.Web/src/chat/SendFriendRequestModal.tsx`
- Modify: `src/Attic.Web/src/auth/DeleteAccountModal.tsx`

- [ ] **Step 17.1: `CreateRoomModal.tsx`**

Wrap the existing form in the `Dialog` primitive. The parent (`ChatShell`) already controls `createOpen` state — update the modal to take `open` / `onOpenChange`:

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import type { ApiError } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from '@/components/ui/dialog';

export function CreateRoomModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [kind, setKind] = useState<'public' | 'private'>('public');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => channelsApi.create({ name: name.trim(), description: description.trim() || null, kind }),
    onSuccess: (channel) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${channel.id}`);
      onClose();
    },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Create failed'),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New room</DialogTitle>
          <DialogDescription>Create a public or private channel.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Input placeholder="Name (3-120 chars)" value={name}
                 onChange={e => setName(e.target.value)} maxLength={120} />
          <Textarea placeholder="Description (optional)" value={description}
                    onChange={e => setDescription(e.target.value)} maxLength={1024} rows={2} />
          <div className="flex gap-4 text-sm">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="radio" checked={kind === 'public'} onChange={() => setKind('public')} /> Public
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="radio" checked={kind === 'private'} onChange={() => setKind('private')} /> Private
            </label>
          </div>
          {error && <div className="text-sm text-destructive">{error}</div>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button onClick={() => mutation.mutate()} disabled={mutation.isPending || name.trim().length < 3}>
            {mutation.isPending ? 'Creating…' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 17.2: `SendFriendRequestModal.tsx`** — same pattern: wrap in Dialog, replace inputs with `<Input>`, textarea with `<Textarea>`, buttons with `<Button>`. Search results list styled with `hover:bg-accent` + keyboard selection remains.

- [ ] **Step 17.3: `DeleteAccountModal.tsx`** — Dialog with a destructive-variant confirm Button. Dialog title / description spell out the cascade. Button variant="destructive".

- [ ] **Step 17.4: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/CreateRoomModal.tsx src/Attic.Web/src/chat/SendFriendRequestModal.tsx src/Attic.Web/src/auth/DeleteAccountModal.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): all 3 modals rebuilt on Dialog primitive"
```

---

## Task 18: `PublicCatalog` + `InvitationsInbox` — Card list + Skeleton

**Files:**
- Modify: `src/Attic.Web/src/chat/PublicCatalog.tsx`
- Modify: `src/Attic.Web/src/chat/InvitationsInbox.tsx`

- [ ] **Step 18.1: `PublicCatalog.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Search, Users, LogIn } from 'lucide-react';
import { channelsApi } from '../api/channels';
import { usePublicCatalog } from './usePublicCatalog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';

export function PublicCatalog() {
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, isLoading } = usePublicCatalog(search);
  const items = (data?.pages ?? []).flatMap(p => p.items);

  const join = useMutation({
    mutationFn: (id: string) => channelsApi.join(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${id}`);
    },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4">Public rooms</h1>
      <div className="relative mb-4">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9" placeholder="Search by name prefix…" value={search}
               onChange={e => setSearch(e.target.value)} />
      </div>
      <div className="rounded-lg border bg-card divide-y">
        {isLoading && Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="p-4 flex justify-between">
            <div className="space-y-2">
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-3 w-64" />
            </div>
            <Skeleton className="h-8 w-16" />
          </div>
        ))}
        {!isLoading && items.length === 0 && (
          <div className="p-8 text-center text-muted-foreground text-sm">No rooms yet — create one.</div>
        )}
        {items.map(c => (
          <div key={c.id} className="flex items-center justify-between px-4 py-3">
            <div className="min-w-0">
              <div className="font-medium truncate">{c.name}</div>
              <div className="text-sm text-muted-foreground flex items-center gap-1">
                {c.description ?? '—'}
                <span className="mx-1">·</span>
                <Users className="h-3 w-3" /> {c.memberCount}
              </div>
            </div>
            <Button onClick={() => join.mutate(c.id)} disabled={join.isPending}>
              <LogIn className="h-4 w-4" />Join
            </Button>
          </div>
        ))}
      </div>
      {hasNextPage && (
        <Button variant="ghost" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}
                className="mt-4 self-center">
          {isFetchingNextPage ? 'Loading…' : 'Load more'}
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 18.2: `InvitationsInbox.tsx`** — similar treatment: Card-like list with Skeleton loader, lucide icons on Accept (Check) / Decline (X) buttons.

- [ ] **Step 18.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/PublicCatalog.tsx src/Attic.Web/src/chat/InvitationsInbox.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): PublicCatalog + InvitationsInbox card list + Skeleton"
```

---

## Task 19: `Contacts` page — Tabs + UserAvatar + DropdownMenu

**Files:**
- Modify: `src/Attic.Web/src/chat/Contacts.tsx`

Swap the three sections for Tabs (Incoming, Outgoing, Friends) with a count badge in each trigger, add UserAvatar on each row, collapse the per-friend buttons into a DropdownMenu (Chat / Remove / Block).

- [ ] **Step 19.1: Replace the file**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MessageCircle, UserMinus, Ban, UserPlus, Check, X, MoreHorizontal } from 'lucide-react';
import { friendsApi } from '../api/friends';
import { usersApi } from '../api/users';
import { useAuth } from '../auth/useAuth';
import { useFriends } from './useFriends';
import { useFriendRequests } from './useFriendRequests';
import { useOpenPersonalChat } from './useOpenPersonalChat';
import { SendFriendRequestModal } from './SendFriendRequestModal';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { UserAvatar } from '@/components/ui/avatar';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';

export function Contacts() {
  const { user } = useAuth();
  const qc = useQueryClient();
  const openChat = useOpenPersonalChat();
  const [modalOpen, setModalOpen] = useState(false);

  const { data: friends } = useFriends();
  const { data: requests } = useFriendRequests();
  const incoming = (requests ?? []).filter(r => r.recipientId === user?.id);
  const outgoing = (requests ?? []).filter(r => r.senderId === user?.id);

  const accept = useMutation({
    mutationFn: (id: string) => friendsApi.accept(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friend-requests'] });
      void qc.invalidateQueries({ queryKey: ['friends'] });
    },
  });
  const decline = useMutation({
    mutationFn: (id: string) => friendsApi.decline(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friend-requests'] }); },
  });
  const remove = useMutation({
    mutationFn: (userId: string) => friendsApi.removeFriend(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });
  const block = useMutation({
    mutationFn: (userId: string) => usersApi.block(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Contacts</h1>
        <Button onClick={() => setModalOpen(true)}>
          <UserPlus className="h-4 w-4" />Send friend request
        </Button>
      </div>

      <Tabs defaultValue="friends">
        <TabsList>
          <TabsTrigger value="friends">
            Friends <Badge variant="secondary" className="ml-2 h-5">{friends?.length ?? 0}</Badge>
          </TabsTrigger>
          <TabsTrigger value="incoming">
            Incoming <Badge variant="secondary" className="ml-2 h-5">{incoming.length}</Badge>
          </TabsTrigger>
          <TabsTrigger value="outgoing">
            Outgoing <Badge variant="secondary" className="ml-2 h-5">{outgoing.length}</Badge>
          </TabsTrigger>
        </TabsList>

        <TabsContent value="friends">
          {(friends ?? []).length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No friends yet — send a request to get started.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {(friends ?? []).map(f => (
                <li key={f.userId} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <UserAvatar username={f.username} />
                    <div>
                      <div className="font-medium">{f.username}</div>
                      <div className="text-xs text-muted-foreground">
                        Friends since {new Date(f.friendsSince).toLocaleDateString()}
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button variant="outline" size="sm" onClick={() => openChat(f.username)}>
                      <MessageCircle className="h-3.5 w-3.5" />Chat
                    </Button>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon"><MoreHorizontal className="h-4 w-4" /></Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => remove.mutate(f.userId)}>
                          <UserMinus className="h-4 w-4" />Remove friend
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem onClick={() => block.mutate(f.userId)} className="text-destructive focus:text-destructive">
                          <Ban className="h-4 w-4" />Block user
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="incoming">
          {incoming.length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No incoming requests.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {incoming.map(r => (
                <li key={r.id} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <UserAvatar username={r.senderUsername} />
                    <div>
                      <div className="font-medium">{r.senderUsername}</div>
                      {r.text && <div className="text-sm text-muted-foreground">{r.text}</div>}
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button size="sm" onClick={() => accept.mutate(r.id)}>
                      <Check className="h-3.5 w-3.5" />Accept
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => decline.mutate(r.id)}>
                      <X className="h-3.5 w-3.5" />Decline
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>

        <TabsContent value="outgoing">
          {outgoing.length === 0 ? (
            <div className="p-8 text-muted-foreground text-sm text-center border rounded-lg bg-card">
              No outgoing requests.
            </div>
          ) : (
            <ul className="divide-y border rounded-lg bg-card">
              {outgoing.map(r => (
                <li key={r.id} className="flex items-center gap-3 px-4 py-3">
                  <UserAvatar username={r.recipientUsername} />
                  <div>
                    <div className="font-medium">{r.recipientUsername}</div>
                    <div className="text-xs text-muted-foreground">
                      Pending since {new Date(r.createdAt).toLocaleString()}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </TabsContent>
      </Tabs>

      {modalOpen && <SendFriendRequestModal onClose={() => setModalOpen(false)} />}
    </div>
  );
}
```

- [ ] **Step 19.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/Contacts.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): Contacts page with Tabs + UserAvatar + DropdownMenu actions"
```

---

## Task 20: `RoomDetails` — Avatar + Badge role + DropdownMenu + presence dot

**Files:**
- Modify: `src/Attic.Web/src/chat/RoomDetails.tsx`

Replace the existing rendering — the Phase 5 `MemberRow` subcomponent already exists for hook rules. Update its styling.

- [ ] **Step 20.1: Rewrite `RoomDetails.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { LogOut, Trash2, UserPlus, ChevronUp, ChevronDown, Ban, MoreHorizontal } from 'lucide-react';
import { channelsApi } from '../api/channels';
import { invitationsApi } from '../api/invitations';
import { useAuth } from '../auth/useAuth';
import { useChannelDetails } from './useChannelDetails';
import { useChannelMembers } from './useChannelMembers';
import { usePresence } from './usePresence';
import type { ChannelMemberSummary } from '../types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { ScrollArea } from '@/components/ui/scroll-area';
import { UserAvatar } from '@/components/ui/avatar';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { cn } from '@/lib/utils';

export function RoomDetails({ channelId }: { channelId: string }) {
  const { user } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const { data: details } = useChannelDetails(channelId);
  const { data: members } = useChannelMembers(channelId);

  const selfRole = members?.find(m => m.userId === user?.id)?.role;
  const canManage = selfRole === 'owner' || selfRole === 'admin';
  const isOwner = selfRole === 'owner';

  const leave = useMutation({
    mutationFn: () => channelsApi.leave(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const del = useMutation({
    mutationFn: () => channelsApi.delete(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const ban = useMutation({
    mutationFn: (userId: string) => channelsApi.banMember(channelId, userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });
  const toggleRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: 'admin' | 'member' }) =>
      channelsApi.changeRole(channelId, userId, role),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });

  const [inviteUsername, setInviteUsername] = useState('');
  const invite = useMutation({
    mutationFn: () => invitationsApi.issue(channelId, { username: inviteUsername.trim() }),
    onSuccess: () => setInviteUsername(''),
  });

  return (
    <aside className="w-72 border-l bg-card text-sm flex flex-col">
      <div className="p-4 border-b">
        <div className="font-semibold text-base">{details?.name}</div>
        <div className="text-xs text-muted-foreground flex items-center gap-2">
          <Badge variant="outline">{details?.kind}</Badge>
          <span>{details?.memberCount} members</span>
        </div>
        {details?.description && <p className="text-sm text-muted-foreground mt-2">{details.description}</p>}
      </div>

      {details?.kind === 'private' && canManage && (
        <div className="p-4 border-b space-y-2">
          <div className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Invite</div>
          <div className="flex gap-2">
            <Input value={inviteUsername} onChange={e => setInviteUsername(e.target.value)}
                   placeholder="Username" className="h-8" />
            <Button size="sm" onClick={() => invite.mutate()} disabled={invite.isPending || !inviteUsername.trim()}>
              <UserPlus className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>
      )}

      <div className="px-4 py-2 text-xs font-semibold text-muted-foreground uppercase tracking-wide">Members</div>
      <ScrollArea className="flex-1">
        <ul className="px-2 pb-2 space-y-1">
          {members?.map(m => (
            <MemberRow
              key={m.userId}
              m={m}
              selfId={user?.id}
              canManage={canManage}
              onToggleRole={role => toggleRole.mutate({ userId: m.userId, role })}
              onBan={() => ban.mutate(m.userId)}
            />
          ))}
        </ul>
      </ScrollArea>

      <Separator />
      <div className="p-4 space-y-2">
        {!isOwner && (
          <Button variant="ghost" className="w-full justify-start" onClick={() => leave.mutate()}>
            <LogOut className="h-4 w-4" />Leave room
          </Button>
        )}
        {isOwner && (
          <Button variant="ghost" className="w-full justify-start text-destructive hover:text-destructive"
                  onClick={() => del.mutate()}>
            <Trash2 className="h-4 w-4" />Delete room
          </Button>
        )}
      </div>
    </aside>
  );
}

function MemberRow({
  m, selfId, canManage, onToggleRole, onBan,
}: {
  m: ChannelMemberSummary;
  selfId: string | undefined;
  canManage: boolean;
  onToggleRole: (role: 'admin' | 'member') => void;
  onBan: () => void;
}) {
  const presence = usePresence(m.userId);
  const dot = presence === 'online' ? 'bg-green-500'
            : presence === 'afk' ? 'bg-yellow-500'
            : 'bg-muted-foreground/40';
  const roleVariant: 'default' | 'secondary' | 'outline' =
    m.role === 'owner' ? 'default' : m.role === 'admin' ? 'secondary' : 'outline';

  const canEdit = canManage && m.userId !== selfId && m.role !== 'owner';

  return (
    <li className="flex items-center justify-between rounded-md px-2 py-1 hover:bg-accent hover:text-accent-foreground">
      <div className="flex items-center gap-2 min-w-0">
        <div className="relative">
          <UserAvatar username={m.username} className="h-7 w-7" />
          <span className={cn('absolute -bottom-0.5 -right-0.5 h-2 w-2 rounded-full ring-2 ring-card', dot)} />
        </div>
        <span className="truncate">{m.username}</span>
        <Badge variant={roleVariant} className="text-[10px] py-0 px-1.5">{m.role}</Badge>
      </div>
      {canEdit && (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="h-6 w-6 opacity-60 hover:opacity-100">
              <MoreHorizontal className="h-3.5 w-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {m.role === 'member' ? (
              <DropdownMenuItem onClick={() => onToggleRole('admin')}>
                <ChevronUp className="h-4 w-4" />Promote to admin
              </DropdownMenuItem>
            ) : (
              <DropdownMenuItem onClick={() => onToggleRole('member')}>
                <ChevronDown className="h-4 w-4" />Demote to member
              </DropdownMenuItem>
            )}
            <DropdownMenuItem onClick={onBan} className="text-destructive focus:text-destructive">
              <Ban className="h-4 w-4" />Ban
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      )}
    </li>
  );
}
```

- [ ] **Step 20.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/RoomDetails.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): RoomDetails with Avatar + Badge roles + DropdownMenu + presence"
```

---

## Task 21: `Sessions` page polish

**Files:**
- Modify: `src/Attic.Web/src/auth/Sessions.tsx`

- [ ] **Step 21.1: Rewrite**

```tsx
import { useEffect } from 'react';
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Laptop, Smartphone, Monitor } from 'lucide-react';
import { sessionsApi } from '../api/sessions';
import { getOrCreateHubClient, disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';

function deviceIcon(userAgent: string) {
  const ua = userAgent.toLowerCase();
  if (ua.includes('mobile') || ua.includes('iphone') || ua.includes('android')) return Smartphone;
  if (ua.includes('mac') || ua.includes('windows') || ua.includes('linux')) return Laptop;
  return Monitor;
}

export function Sessions() {
  const qc = useQueryClient();
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({
    queryKey: ['sessions'] as const,
    queryFn: () => sessionsApi.listMine(),
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onForceLogout(() => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    });
    return () => { off(); };
  }, [navigate, setUser]);

  const revoke = useMutation({
    mutationFn: (id: string) => sessionsApi.revoke(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['sessions'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto bg-background">
      <h1 className="text-xl font-semibold mb-4">Active sessions</h1>
      {isLoading && (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="p-4 border rounded-lg bg-card flex gap-3">
              <Skeleton className="h-6 w-6 rounded" />
              <div className="flex-1 space-y-2"><Skeleton className="h-4 w-48" /><Skeleton className="h-3 w-32" /></div>
            </div>
          ))}
        </div>
      )}
      <ul className="space-y-2">
        {(data ?? []).map(s => {
          const Icon = deviceIcon(s.userAgent);
          return (
            <li key={s.id} className="flex items-center gap-3 border rounded-lg bg-card px-4 py-3">
              <Icon className="h-5 w-5 text-muted-foreground" />
              <div className="flex-1 min-w-0">
                <div className="font-medium flex items-center gap-2">
                  <span className="truncate">{s.userAgent || 'Unknown client'}</span>
                  {s.isCurrent && <Badge variant="default">This tab</Badge>}
                </div>
                <div className="text-xs text-muted-foreground">
                  {s.ip ?? '?'} · last seen {new Date(s.lastSeenAt).toLocaleString()}
                </div>
              </div>
              {!s.isCurrent && (
                <Button variant="ghost" size="sm" onClick={() => revoke.mutate(s.id)}
                        className="text-destructive hover:text-destructive">
                  Revoke
                </Button>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
```

- [ ] **Step 21.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/auth/Sessions.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "refactor(web): Sessions page with device icons + Skeleton + Badge"
```

---

## Task 22: Toast integration on mutations

**Files:**
- Modify: `src/Attic.Web/src/chat/useDeleteMessage.ts`
- Modify: `src/Attic.Web/src/chat/useEditMessage.ts`
- Modify: `src/Attic.Web/src/chat/useUploadAttachments.ts`

Call `toast.error(...)` from `sonner` on failures. `sonner` exports `toast` as a top-level function.

- [ ] **Step 22.1: `useDeleteMessage.ts`**

Catch the error thrown by `hub.deleteMessage` and toast:

```ts
import { toast } from 'sonner';
// inside the async callback's catch (wrap the existing body):
try {
  const ack = await hub.deleteMessage(messageId);
  if (!ack.ok) throw new Error(ack.error ?? 'delete_failed');
  // ...existing cache update...
} catch (e) {
  toast.error('Could not delete message', { description: (e as Error).message });
  throw e;
}
```

- [ ] **Step 22.2: `useEditMessage.ts`** — same pattern on `editMessage` failure.

- [ ] **Step 22.3: `useUploadAttachments.ts`** — `toast.error('Upload failed', ...)` on the `setPending(prev => ...status: 'error')` branch.

- [ ] **Step 22.4: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/useDeleteMessage.ts src/Attic.Web/src/chat/useEditMessage.ts src/Attic.Web/src/chat/useUploadAttachments.ts docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): toast on message delete/edit failures + upload errors"
```

---

## Task 23: Checkpoint 3 marker

```bash
git commit --allow-empty -m "chore: Phase 7 Checkpoint 3 (feature refactor) green"
```

---

## Task 24: Auth-gate screen loading shell

**Files:**
- Modify: `src/Attic.Web/src/auth/AuthGate.tsx`

The auth gate currently shows a blank screen while `/api/auth/me` resolves. Add a centered spinner / logo.

- [ ] **Step 24.1: Update `AuthGate.tsx`**

Replace the "loading" branch of the component with:

```tsx
import { Loader2 } from 'lucide-react';
// ...
if (loading) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
    </div>
  );
}
```

Preserve the redirect logic for unauthenticated users. Read the existing file first and only modify the loading branch.

- [ ] **Step 24.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/auth/AuthGate.tsx docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "feat(web): auth-gate loading state shows a spinner"
```

---

## Task 25: Sweep any leftover Tailwind colors

**Files:**
- Modify: any component still referencing `bg-white`, `bg-slate-50`, `bg-slate-100`, `text-slate-*`, `border-slate-*`

- [ ] **Step 25.1: Grep and patch**

Run `rg -l "bg-white|bg-slate-|text-slate-|border-slate-" src/Attic.Web/src/` and replace occurrences:
- `bg-white` → `bg-card` (or `bg-background` for page-level)
- `bg-slate-50` → `bg-muted/30` or `bg-background`
- `bg-slate-100` → `bg-muted`
- `text-slate-500` / `text-slate-600` → `text-muted-foreground`
- `text-slate-400` → `text-muted-foreground/70`
- `border-slate-200` / `border-slate-300` → `border-border`
- `text-blue-600` / `text-blue-700` → `text-primary`

Skim each file after the replace pass to verify no regressions in contrast or layout.

- [ ] **Step 25.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add -u src/Attic.Web/src docs/superpowers/plans/2026-04-21-phase7-frontend-polish.md
git commit -m "style(web): sweep remaining Tailwind colors onto design tokens"
```

---

## Task 26: Smoke test

- [ ] **Step 26.1: Full backend + frontend**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 117 domain + 66 integration = 183 green. Frontend build 0 errors.

- [ ] **Step 26.2: Checkpoint marker**

```bash
git commit --allow-empty -m "chore: Phase 7 end-to-end smoke green"
```

---

## Task 27: Final Phase 7 marker

```bash
git commit --allow-empty -m "chore: Phase 7 complete — shadcn/ui + lucide + dark mode"
```

---

## Phase 7 completion checklist

- [x] shadcn/ui primitives installed: Button, Input, Textarea, Dialog, DropdownMenu, Tabs, Tooltip, Avatar, Badge, Separator, ScrollArea, Skeleton, Toaster
- [x] Design-token system in `index.css` (light + dark) via Tailwind 4 `@theme`
- [x] `ThemeProvider` + `ThemeToggle` (light / dark / system)
- [x] `Toaster` (sonner) mounted at app root; error toasts wired on message delete/edit/upload failures
- [x] All 3 modals refactored onto `Dialog` (CreateRoom, SendFriendRequest, DeleteAccount)
- [x] `Sidebar` uses `Tabs` + `ScrollArea` + lucide icons + `Badge` for unread counts
- [x] `MessageActionsMenu` refactored onto `DropdownMenu`
- [x] `ChatShell` header: `UserAvatar` + `DropdownMenu` (Sign out / Delete account) + `ThemeToggle`
- [x] `ChatWindow` messages: `UserAvatar` + inline edit via `Input` + reply-context styling
- [x] `ChatInput`: lucide Paperclip / Send icons, `Textarea`, upload chips as `Badge`
- [x] `Contacts`: `Tabs` (Friends/Incoming/Outgoing) + `UserAvatar` + per-friend `DropdownMenu`
- [x] `RoomDetails`: `Avatar` with presence dot + `Badge` role + `DropdownMenu` member actions
- [x] `PublicCatalog` + `InvitationsInbox`: Card-like list + `Skeleton` loader
- [x] `Sessions`: device icon + `Skeleton` + current-session `Badge`
- [x] Login + Register: centered card with icon header, `Input` + `Button`
- [x] Auth-gate loading spinner
- [x] Leftover Tailwind colors swept onto semantic tokens

## What's intentionally NOT in this phase

- Motion / page transitions (Framer Motion) — deferred. Radix primitives already provide subtle enter/exit via `tailwindcss-animate`.
- Command palette (Cmd-K) — deferred. Could use `cmdk` later.
- Accessibility audit pass (axe / Lighthouse) — Radix gives correct a11y semantics by default; a formal audit is a separate effort.
- SVG logo / illustrations for empty states — deferred.
- Upload progress bar inside each chip (currently just a spinner) — deferred.
