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

## Scenarios

Golden-path / realtime:

- `register-create-post.spec.ts` — register → create room → send message → reload → persists.
- `invite-and-realtime.spec.ts` — two contexts, private-room invite accepted, realtime message.
- `attachment-access.spec.ts` — image upload, per-membership download authorization.
- `unread-counter.spec.ts` — two contexts; UnreadChanged badge increments over the hub; MarkRead resets it.

Messaging actions:

- `edit-message.spec.ts` — sender edits their own message, `(edited)` marker + new content render.
- `delete-message.spec.ts` — sender deletes their own message, row disappears, others stay.
- `reply-to.spec.ts` — reply composer quotes the original as context on the reply row.

Rooms:

- `join-catalog.spec.ts` — user browses `/catalog`, joins someone else's public room, posts there.

Friends + blocks:

- `friend-dm.spec.ts` — friend-request → accept → open DM → message delivers.
- `block-denies-post.spec.ts` — after a block the friendship is removed on both sides.

Auth + UI affordances:

- `forgot-password.spec.ts` — the dialog submits cleanly and the UI stays on `/login`.
- `theme-toggle.spec.ts` — toggling the theme updates `<html>`'s class and persists across reload.

Emoji-picker regression:

- `emoji-picker.spec.ts` — regression guard for the `left-0` positioning fix (commit 8eb752a):
  the picker lands inside MAIN's clip region and clicking a tile inserts the emoji into the composer.

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
