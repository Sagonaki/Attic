# Attic — Web Frontend

React 19 + Vite + Tailwind 4 SPA for the Attic chat application.

Not a standalone app. Runs under .NET Aspire orchestration alongside the ASP.NET Core API, Postgres, and Redis. See the repo root for the full stack and design spec.

## Scripts

- `npm run dev` — Vite dev server on `:3000`. Proxies `/api` and `/hub` to the API (URL comes from Aspire env vars, falls back to `http://localhost:5000`).
- `npm run build` — Production build to `dist/`.
- `npm run lint` — `tsc --noEmit` typecheck.

## Stack

- React 19, TanStack Query v5, React Router v6, `@microsoft/signalr` v8, Tailwind CSS v4.
- State: TanStack Query cache for REST; SignalR pushes into the same cache.
- Auth: server-set cookie; the SPA gates routes with `AuthGate` and exposes `useAuth()`.
