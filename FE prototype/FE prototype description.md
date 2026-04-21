# Attic Chat Portal - Technical Solution Specification

## 1. System Overview
Attic is a high-performance, modular chat application. This document serves as a blueprint for AI agents to understand, maintain, or extend the codebase.

## 2. Directory Structure & Architecture
The project follows a fractal component structure for high cohesion and low coupling.

```text
src/
├── components/
│   ├── auth/         # Authentication flows (SignIn, Register, Password Reset)
│   ├── chat/         # Core messaging UI (Window, RoomDetails, Input)
│   ├── contacts/     # Relationship & Invitation management
│   ├── layout/       # Structural shell (Header, Sidebar)
│   ├── modals/       # Orchestration modals (Create/Manage Room)
│   ├── profile/      # User settings & account management
│   ├── sessions/     # Security & device session monitoring
│   └── ui/           # Atom components (Button, Input, primitive wrappers)
├── constants.ts      # Shared config (LOGO_URL, EMOJIS, UI limits)
├── types.ts          # Centralized Type Definitions & Interfaces
├── App.tsx           # Root Orchestrator & Global State Container
└── index.css         # Tailwind @theme and global resets
```

## 3. Core Domain Models (`src/types.ts`)
Agents MUST adhere to these interfaces when extending data logic:
- `Message`: Supports `text`, `file`, and `reply` types. Includes threading via `replyToId`.
- `Room`: Distinguished by `public` or `private` types. Includes member counts and unread state.
- `Contact`: Includes presence states (`online`, `offline`, `afk`).
- `FriendRequest`: Handles both `incomingRequests` and `outgoingRequests`.
- `Session`: Metadata for device security (IP, Location, User Agent).

## 4. Component Technical Specs

### Messaging Engine (`ChatWindow.tsx`)
- **State Props**: Receives `messages`, `typingUser`, and `activeContact`.
- **Scrolling Logic**: Uses a `useRef` on the scroll container. Implements "Smart Scroll" (locks to bottom unless manually scrolled up).
- **Infinite Scroll Pattern**:
  - Event listener on `scrollTop === 0`.
  - `isLoadingMore` state prevents duplicate triggers.
  - Injects `oldMessages` at the start of the array while maintaining scroll offset.
- **Context Actions**: Passes callback triggers for `onStartReply` and `onStartEdit` to populate the global `App` state.

### Layout Orchestration (`App.tsx`)
- **View Routing**: Uses a string-based `activeView` state (`chat` | `contacts` | `profile` | etc.) wrapped in Framer Motion's `AnimatePresence`.
- **Mode Switching**: The `chatCategory` (`public` | `private` | `personal`) dictates the filtering logic for the sidebar and the visibility of the right panel.
- **Panel Visibility**: `RoomDetails` is conditionally rendered using `{chatCategory !== 'personal' && <RoomDetails />}`.

### Relationship & Networking (`ContactsView.tsx`)
- **Data Mapping**: Separates `incomingRequests` and `outgoingRequests`.
- **States**: Handles Empty, Pending, and Accepted states with specific UI treatments.

## 5. UI & Styling Standards
- **Theme**: Defined in `src/index.css` under `@theme`. Replaces traditional `tailwind.config.js` with CSS variables for dynamic theming.
- **Accessibility**: All interactive elements use `aria-label` or high-contrast focus rings (`focus:ring-2 focus:ring-blue-500`).
- **Animations**: Standard entry/exit variants:
  ```typescript
  initial={{ opacity: 0, scale: 0.95 }}
  animate={{ opacity: 1, scale: 1 }}
  transition={{ duration: 0.2 }}
  ```

## 6. Logic Implementations for Agents

### Adding a New View
1. Add the view name to `View` type in `types.ts`.
2. Create a folder in `components/`.
3. Export a functional component.
4. Add a navigation trigger in `Header.tsx`.
5. Update the switch-case in `App.tsx`.

### Extending Message Types
1. Update `Message['type']` in `types.ts`.
2. Add a rendering branch in `ChatWindow.tsx` inside the message list map.
3. Update the `Input` handling in `App.tsx` / `ChatWindow.tsx` to handle the new payload.

## 7. Security Patterns
- **Session Control**: Session revocation requires a multi-select state (`selectedSessions`) and a confirmation callback.
- **Privacy Mode**: Personal chats inherit the `isChatFrozen` logic if a user is blocked, disabling the entire input area.
- **Admin Gates**: `ManageRoomModal` uses a `isAdmin` check (simulated via local state) before allowing write operations on members/bans.
