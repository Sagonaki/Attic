# Attic Web E2E

Playwright end-to-end tests that drive a real Chromium browser against the Aspire AppHost.

## Prerequisites

- Node.js 20+
- A running Aspire AppHost (`dotnet run --project src/Attic.AppHost` from the repo root).
- `npx playwright install chromium` (one-time, after `npm install`).

## Run

From this directory:

```bash
# Point tests at the URL where Aspire is serving the SPA.
export E2E_BASE_URL=https://localhost:7051   # or whatever port Aspire chose
npm test
```

`playwright.config.ts` defaults `E2E_BASE_URL` to `https://localhost:7051` so if your
Aspire run happens to bind there, you can skip the export.

## Scenarios (32 functional + 1 stress)

Golden path / realtime:

- `register-create-post.spec.ts` — register → create room → send → reload.
- `reload-preserves-session.spec.ts` — reload keeps cookie-auth alive.
- `invite-and-realtime.spec.ts` — private-room invite + accept + realtime broadcast.
- `attachment-access.spec.ts` — image upload, per-membership download gating.
- `unread-counter.spec.ts` — UnreadChanged badge + MessageCreated fan-out.
- `realtime-echo.spec.ts` — peer's send arrives in the receiver's chat window.
- `catalog-refresh.spec.ts` — newly-created public room is listed in `/catalog`.

Messaging actions:

- `edit-message.spec.ts` — sender edit + `(edited)` marker.
- `delete-message.spec.ts` — sender delete, row disappears.
- `reply-to.spec.ts` — reply row renders the quoted original.
- `admin-delete-other.spec.ts` — owner can delete another user's message.
- `content-size-limit.spec.ts` — oversize message (> 3 KB) rejected.

Rooms:

- `join-catalog.spec.ts` — browse + join public.
- `leave-room.spec.ts` — non-owner leaves → room gone from sidebar.
- `owner-delete-room.spec.ts` — owner deletes their own room.
- `direct-private-link.spec.ts` — outsider direct-linking to a private channel sees no leak.

Friends + blocks:

- `friend-dm.spec.ts` — request → accept → DM.
- `block-denies-post.spec.ts` — block removes the friendship on both sides.
- `decline-friend-request.spec.ts` — B declines → A's outgoing cleared.
- `unblock-restores.spec.ts` — unblock clears the block but doesn't auto-refriend.
- `user-search.spec.ts` — send-friend-request modal filters by username prefix.
- `decline-invitation.spec.ts` — invitee declines → room not in their private tab.

Auth + profile:

- `logout-relogin.spec.ts` — sign out → sign back in with the same credentials.
- `change-password.spec.ts` — change password via `/profile` + re-login.
- `delete-account.spec.ts` — delete account → cannot log back in.
- `session-revoke.spec.ts` — revoke another session → kicked tab redirects to `/login`. **Known-flaky in the serialized run; product fix validated by integration test; marked `test.fixme` until the race is resolved.**
- `forgot-password.spec.ts` — dialog submits cleanly, stays on `/login`.
- `unauth-redirect.spec.ts` — unauthenticated visits to protected routes bounce to `/login`.

Attachments:

- `paste-attachment.spec.ts` — clipboard paste → upload + send.
- `drag-drop-attachment.spec.ts` — drag-and-drop file → upload + send.
- `non-image-attachment.spec.ts` — non-image file renders as a file chip (not `<img>`).

UI:

- `theme-toggle.spec.ts` — theme switch + reload persistence.
- `emoji-picker.spec.ts` — regression for the `left-0` positioning fix (commit 8eb752a).

## Development

- `npm run test:ui` — interactive Playwright UI.
- `npm run test:headed` — watch the browser window.
- `npm run report` — open the HTML report from the last run.

## CI note

Per spec §12.4, the CI approach is: `dotnet test` first (unit + integration), then bring up
the AppHost and run `npx playwright test` against it. Not wired yet — that's a future
deployment hardening task.

## Interactive dev with Playwright MCP

If you're using a Claude Code session that has the Playwright MCP attached (plugin:playwright),
you can drive the same browser the tests use without writing a full spec:

1. Start the AppHost and note its SPA URL.
2. In the Claude Code session, call `browser_navigate` with the URL.
3. Use `browser_snapshot` / `browser_click` / `browser_type` / `browser_take_screenshot` to inspect and interact.
4. When something feels flaky, codify it as a new scenario in `tests/`.

## Stress test (30 parallel contexts)

Run `STRESS_CONTEXTS=30 npx playwright test stress.spec.ts`. Catches SPA-side regressions under
concurrent load. Scale up cautiously — each context allocates hundreds of MB; 30 is the practical
ceiling on most dev machines.

For protocol-level 300-user load (headless), see `tests/Attic.Web.LoadTests/`.
