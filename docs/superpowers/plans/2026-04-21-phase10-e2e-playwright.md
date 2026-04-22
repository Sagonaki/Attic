# Attic Phase 10 — E2E Playwright Tests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development.

**Goal:** Stand up the `tests/Attic.Web.E2E/` project promised by spec §12.3 with three golden-path scenarios that drive the real UI in a real browser against the running Aspire AppHost. The suite doubles as a manual smoke harness invokable from the Playwright MCP in this session.

**Architecture:** A TypeScript Node project with `@playwright/test`, isolated under `tests/Attic.Web.E2E/`. No code shared with the React app — E2E tests target the public URLs only. `playwright.config.ts` reads `E2E_BASE_URL` from the environment (defaulting to a sensible dev value) so the tests can run against any hostname / port Aspire picks today. Browsers are Chromium only for MVP. Fixtures provide reusable helpers: `registerFreshUser(page)` wraps the sign-up flow; `createChannel(page, kind, name)` wraps the sidebar create-room modal. Tests don't restart Aspire themselves — the developer runs `dotnet run --project src/Attic.AppHost` in a separate terminal and exports the public URL before invoking `npx playwright test`. This keeps the runtime responsibility out of the test project and mirrors the CI approach the spec calls out in §12.4.

**Tech stack additions:** `@playwright/test` + browser binaries. No backend dependencies, no changes to the API or SPA.

**Spec reference:** §3.1 (project layout), §12.3 (E2E scenarios), §12.4 (CI invocation).

---

## Prerequisites

- All 183+ prior tests remain green. No backend or frontend changes in this phase.
- The Aspire AppHost is runnable (`dotnet run --project src/Attic.AppHost`) and serves the SPA. That's an operational precondition for running the tests — not enforced by the tests themselves.
- The Playwright MCP is attached to this session (`mcp__plugin_playwright_playwright__*` tools).

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-10` (branched from `main` after Phase 9 + logo commit).
- `dotnet test tests/Attic.Domain.Tests` → 117 passing.
- `cd src/Attic.Web && npm run lint && npm run build` clean.
- `tests/Attic.Web.E2E/` does not yet exist.

---

## File structure additions

```
tests/
└── Attic.Web.E2E/                                    (new)
    ├── package.json
    ├── tsconfig.json
    ├── playwright.config.ts
    ├── .gitignore                                    (test-results/, playwright-report/, node_modules/)
    ├── README.md                                     (how to run)
    ├── fixtures/
    │   ├── test.ts                                   (custom test fixture extending @playwright/test)
    │   ├── users.ts                                  (registerFreshUser helper)
    │   └── channels.ts                               (createChannel, sendMessage helpers)
    └── tests/
        ├── register-create-post.spec.ts              (scenario 1)
        ├── invite-and-realtime.spec.ts               (scenario 2)
        └── attachment-access.spec.ts                 (scenario 3)
```

One ~10-line entry in the repo `.gitignore` if Playwright artifacts aren't already covered.

---

## Task ordering rationale

Two checkpoints:

- **Checkpoint 1 — Project scaffolding (Tasks 1-5):** Node project setup, Playwright install, config, fixtures, `.gitignore`.
- **Checkpoint 2 — Scenarios + verification (Tasks 6-10):** Three golden-path specs, README, run-through via Playwright MCP against the running AppHost, commit.

---

## Task 1: Initialize the Node project

**Files:**
- Create: `tests/Attic.Web.E2E/package.json`
- Create: `tests/Attic.Web.E2E/tsconfig.json`
- Create: `tests/Attic.Web.E2E/.gitignore`

- [ ] **Step 1.1: Write `package.json`**

```json
{
  "name": "attic-web-e2e",
  "private": true,
  "type": "module",
  "version": "0.0.0",
  "scripts": {
    "test": "playwright test",
    "test:ui": "playwright test --ui",
    "test:headed": "playwright test --headed",
    "report": "playwright show-report"
  },
  "devDependencies": {
    "@playwright/test": "^1.48.0",
    "@types/node": "^22.0.0",
    "typescript": "^5.6.0"
  }
}
```

- [ ] **Step 1.2: Write `tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ES2022",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "resolveJsonModule": true,
    "allowSyntheticDefaultImports": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["node"],
    "baseUrl": "."
  },
  "include": ["**/*.ts"],
  "exclude": ["node_modules", "test-results", "playwright-report"]
}
```

- [ ] **Step 1.3: Write `.gitignore`**

```
node_modules/
test-results/
playwright-report/
blob-report/
playwright/.cache/
*.log
```

- [ ] **Step 1.4: Install dependencies**

```bash
cd tests/Attic.Web.E2E
npm install --legacy-peer-deps
npx playwright install chromium
cd -
```

The `--legacy-peer-deps` flag matches the project-wide preference established in Phase 8.

- [ ] **Step 1.5: Commit (exclude node_modules and browser binaries via .gitignore)**

```bash
git add tests/Attic.Web.E2E/package.json tests/Attic.Web.E2E/tsconfig.json tests/Attic.Web.E2E/.gitignore tests/Attic.Web.E2E/package-lock.json docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "chore(e2e): scaffold Attic.Web.E2E Node project"
```

---

## Task 2: `playwright.config.ts`

**Files:**
- Create: `tests/Attic.Web.E2E/playwright.config.ts`

- [ ] **Step 2.1: Write config**

```ts
import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.E2E_BASE_URL ?? 'https://localhost:7051';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,      // Aspire's DB is shared — keep tests serial until we partition.
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,  // Aspire's dev cert.
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
```

**Key points:**
- `E2E_BASE_URL` is the environment variable you set to match whatever port Aspire chose for the web resource.
- `ignoreHTTPSErrors: true` because Aspire's dev certificate isn't in the OS trust store for Playwright.
- `workers: 1` and `fullyParallel: false` serialize execution against a shared DB.

- [ ] **Step 2.2: Commit**

```bash
git add tests/Attic.Web.E2E/playwright.config.ts docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "chore(e2e): add playwright.config.ts (chromium, env-driven baseURL)"
```

---

## Task 3: Fixtures — `users.ts` + `channels.ts`

**Files:**
- Create: `tests/Attic.Web.E2E/fixtures/users.ts`
- Create: `tests/Attic.Web.E2E/fixtures/channels.ts`
- Create: `tests/Attic.Web.E2E/fixtures/test.ts`

- [ ] **Step 3.1: `users.ts`**

```ts
import { Page, expect } from '@playwright/test';

export interface RegisteredUser {
  email: string;
  username: string;
  password: string;
}

/** Registers a fresh user through the UI register form. Returns the credentials. */
export async function registerFreshUser(page: Page): Promise<RegisteredUser> {
  const suffix = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`;
  const creds: RegisteredUser = {
    email: `e2e-${suffix}@example.test`,
    username: `e2e${suffix}`.slice(0, 20),
    password: 'hunter2pw',
  };
  await page.goto('/register');
  await page.getByPlaceholder('Email').fill(creds.email);
  await page.getByPlaceholder('Username').fill(creds.username);
  await page.getByPlaceholder('Password').fill(creds.password);
  await page.getByRole('button', { name: /register/i }).click();
  // After register, the app redirects to '/'; confirm the shell loaded.
  await expect(page.getByText(creds.username)).toBeVisible({ timeout: 10_000 });
  return creds;
}

/** Logs in an existing user. */
export async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByPlaceholder('Email').fill(email);
  await page.getByPlaceholder('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/$|\/chat\//);
}
```

- [ ] **Step 3.2: `channels.ts`**

```ts
import { Page, expect } from '@playwright/test';

/** Opens the "New room" modal, fills it, submits. Navigates to /chat/<id> on success. */
export async function createChannel(
  page: Page,
  kind: 'public' | 'private',
  name: string,
  description?: string
): Promise<void> {
  await page.getByRole('button', { name: /new room/i }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByPlaceholder(/Name \(3-120/).fill(name);
  if (description) await dialog.getByPlaceholder(/Description/).fill(description);
  if (kind === 'private') {
    await dialog.getByLabel(/private/i).click();
  }
  await dialog.getByRole('button', { name: /^create$/i }).click();
  // On success the modal closes and the URL becomes /chat/<id>.
  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i, { timeout: 10_000 });
}

/** Sends a message through the composer. */
export async function sendMessage(page: Page, text: string): Promise<void> {
  const composer = page.getByPlaceholder(/Type a message/i);
  await composer.fill(text);
  await page.getByRole('button', { name: /send message/i }).click();
  await expect(page.getByText(text).last()).toBeVisible({ timeout: 10_000 });
}
```

- [ ] **Step 3.3: `test.ts`**

A thin re-export is enough — scenarios import `test`, `expect` from `@playwright/test` directly. Create an empty `test.ts` for future fixture composition:

```ts
export { test, expect } from '@playwright/test';
```

- [ ] **Step 3.4: Commit**

```bash
git add tests/Attic.Web.E2E/fixtures docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "chore(e2e): fixtures for register, login, createChannel, sendMessage"
```

---

## Task 4: Scenario 1 — register → create room → post → reload → persists

**Files:**
- Create: `tests/Attic.Web.E2E/tests/register-create-post.spec.ts`

- [ ] **Step 4.1: Write spec**

```ts
import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('register → create room → send message → reload → message persists', async ({ page }) => {
  const user = await registerFreshUser(page);
  const roomName = `room-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', roomName, 'E2E golden-path');

  const content = `hello from ${user.username}`;
  await sendMessage(page, content);

  // Reload the page; the message should still be there.
  await page.reload();
  await expect(page.getByText(content)).toBeVisible({ timeout: 10_000 });
});
```

- [ ] **Step 4.2: Commit**

```bash
git add tests/Attic.Web.E2E/tests/register-create-post.spec.ts docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "test(e2e): scenario 1 — register, create, post, reload, persists"
```

---

## Task 5: Scenario 2 — A invites B to private, B accepts, A sends, B sees in realtime

**Files:**
- Create: `tests/Attic.Web.E2E/tests/invite-and-realtime.spec.ts`

- [ ] **Step 5.1: Write spec**

```ts
import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('A invites B to private, B accepts, A sends, B sees in realtime', async ({ browser }) => {
  // Two independent browser contexts (separate cookie jars = separate users).
  const contextA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const contextB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await contextA.newPage();
  const pageB = await contextB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // A creates a private room.
  const roomName = `priv-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageA, 'private', roomName);

  // A opens the RoomDetails panel (right side) and invites B.
  await pageA.getByRole('button', { name: /invite user/i }).click();
  const dialogA = pageA.getByRole('dialog', { name: /invite to room/i });
  await expect(dialogA).toBeVisible();
  await dialogA.getByPlaceholder(/Search by username/i).fill(userB.username);
  await dialogA.getByRole('button', { name: userB.username }).click();   // pick from results
  await dialogA.getByRole('button', { name: /^send$/i }).click();
  await expect(dialogA).not.toBeVisible({ timeout: 10_000 });

  // B navigates to /invitations and accepts.
  await pageB.goto('/invitations');
  await pageB.getByRole('button', { name: /^accept$/i }).first().click();
  // B is now in the room — the sidebar's Private tab should list it.
  await pageB.getByRole('button', { name: /private/i }).click();
  await expect(pageB.getByRole('link', { name: new RegExp(roomName) })).toBeVisible({ timeout: 15_000 });

  // A sends a message.
  await pageA.bringToFront();
  const msg = `realtime-${Date.now()}`;
  await sendMessage(pageA, msg);

  // B navigates to the room and should see the message arriving via SignalR.
  await pageB.bringToFront();
  await pageB.getByRole('link', { name: new RegExp(roomName) }).click();
  await expect(pageB.getByText(msg)).toBeVisible({ timeout: 15_000 });

  await contextA.close();
  await contextB.close();
});
```

- [ ] **Step 5.2: Commit**

```bash
git add tests/Attic.Web.E2E/tests/invite-and-realtime.spec.ts docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "test(e2e): scenario 2 — invite, accept, realtime message delivery"
```

---

## Task 6: Scenario 3 — Upload image → download → access control

**Files:**
- Create: `tests/Attic.Web.E2E/tests/attachment-access.spec.ts`
- Create: `tests/Attic.Web.E2E/fixtures/fixtures/pixel.png` (tiny 1x1 PNG for upload)

- [ ] **Step 6.1: Create a 1×1 PNG under `fixtures/`**

Generate a minimal valid PNG (content is irrelevant — we only need to exercise the upload path). Write via `echo -ne` or use a checked-in fixture:

```bash
mkdir -p tests/Attic.Web.E2E/fixtures/fixtures
python3 -c "import base64; open('tests/Attic.Web.E2E/fixtures/fixtures/pixel.png','wb').write(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAdBPqMQAAAABJRU5ErkJggg=='))"
```

- [ ] **Step 6.2: Write spec**

```ts
import { test, expect, BrowserContext } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PIXEL_PATH = path.join(__dirname, '..', 'fixtures', 'fixtures', 'pixel.png');

test('upload image → member can download, non-member gets 403', async ({ browser, request }) => {
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  const owner = await registerFreshUser(pageOwner);

  const roomName = `att-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', roomName);

  // Attach the pixel via the hidden file input.
  const fileInput = pageOwner.locator('input[type="file"]').first();
  await fileInput.setInputFiles(PIXEL_PATH);

  // Wait for the upload chip to show "done" (i.e., no uploading spinner).
  await expect(pageOwner.getByText(/pixel\.png/i)).toBeVisible({ timeout: 10_000 });

  // Send the message carrying the attachment.
  await sendMessage(pageOwner, 'here is a pixel');

  // Grab the attachment URL via a <img> element bound to /api/attachments/<id>.
  const attachmentImg = pageOwner.locator('img[src*="/api/attachments/"]').first();
  await expect(attachmentImg).toBeVisible({ timeout: 10_000 });
  const attachmentSrc = await attachmentImg.getAttribute('src');
  if (!attachmentSrc) throw new Error('Missing attachment src');

  // Owner (current session) can fetch the bytes.
  const ownerCookies = await ctxOwner.cookies();
  const cookieHeader = ownerCookies.map(c => `${c.name}=${c.value}`).join('; ');
  const okResp = await request.get(attachmentSrc, { headers: { Cookie: cookieHeader } });
  expect(okResp.status()).toBe(200);

  // Non-member registers in a fresh context and is denied.
  const ctxOther = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOther = await ctxOther.newPage();
  await registerFreshUser(pageOther);
  const otherCookies = await ctxOther.cookies();
  const otherHeader = otherCookies.map(c => `${c.name}=${c.value}`).join('; ');
  const forbidden = await request.get(attachmentSrc, { headers: { Cookie: otherHeader } });
  expect(forbidden.status()).toBe(403);

  await ctxOwner.close();
  await ctxOther.close();
});
```

- [ ] **Step 6.3: Commit**

```bash
git add tests/Attic.Web.E2E/tests/attachment-access.spec.ts tests/Attic.Web.E2E/fixtures/fixtures/pixel.png docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "test(e2e): scenario 3 — upload image, member downloads, non-member 403"
```

---

## Task 7: `README.md` — how to run

**Files:**
- Create: `tests/Attic.Web.E2E/README.md`

- [ ] **Step 7.1: Write**

```markdown
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

- `register-create-post.spec.ts` — register → create room → send message → reload → persists.
- `invite-and-realtime.spec.ts` — two contexts, private-room invite accepted, realtime message.
- `attachment-access.spec.ts` — image upload, per-membership download authorization.

## Development

- `npm run test:ui` — interactive Playwright UI.
- `npm run test:headed` — watch the browser window.
- `npm run report` — open the HTML report from the last run.

## CI note

Per spec §12.4, the CI approach is: `dotnet test` first (unit + integration), then bring up
the AppHost and run `npx playwright test` against it. Not wired yet — that's a future
deployment hardening task.
```

- [ ] **Step 7.2: Commit**

```bash
git add tests/Attic.Web.E2E/README.md docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "docs(e2e): README covering prerequisites + scenarios + run commands"
```

---

## Task 8: Checkpoint 1 marker

- [ ] **Step 8.1: Verify project typechecks**

```bash
cd tests/Attic.Web.E2E
npx tsc --noEmit
cd -
```

Expected: 0 errors.

- [ ] **Step 8.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 10 Checkpoint 1 (scaffolding + scenarios) green"
```

---

## Task 9: Manual smoke via Playwright MCP (documentation only)

**Files:**
- Modify: `tests/Attic.Web.E2E/README.md` (append a note about MCP-based development)

Phase 10's practical payoff is that the main agent (this session) can use the Playwright MCP tools to drive the running AppHost during feature development. No commit beyond the README addition — just demonstrate the capability in the phase wrap-up.

- [ ] **Step 9.1: Append to `README.md`**

```markdown
## Interactive dev with Playwright MCP

If you're using a Claude Code session that has the Playwright MCP attached (plugin:playwright),
you can drive the same browser the tests use without writing a full spec:

1. Start the AppHost and note its SPA URL.
2. In the Claude Code session, call `browser_navigate` with the URL.
3. Use `browser_snapshot` / `browser_click` / `browser_type` / `browser_take_screenshot` to inspect and interact.
4. When something feels flaky, codify it as a new scenario in `tests/`.
```

- [ ] **Step 9.2: Commit**

```bash
git add tests/Attic.Web.E2E/README.md docs/superpowers/plans/2026-04-21-phase10-e2e-playwright.md
git commit -m "docs(e2e): note Playwright MCP workflow for interactive dev"
```

---

## Task 10: Final Phase 10 marker

- [ ] **Step 10.1: Marker**

```bash
git commit --allow-empty -m "chore: Phase 10 complete — E2E Playwright scaffolding + scenarios"
```

---

## Phase 10 completion checklist

- [x] `tests/Attic.Web.E2E/` project scaffolded with `@playwright/test` + TypeScript
- [x] `playwright.config.ts` reads `E2E_BASE_URL`, Chromium-only, serial, HTTPS-tolerant
- [x] Fixtures: `users.ts` (register/login) + `channels.ts` (createChannel/sendMessage)
- [x] Three golden-path scenarios per spec §12.3
- [x] 1×1 pixel PNG fixture for upload tests
- [x] README with prerequisites + run commands + MCP dev notes
- [x] TypeScript clean (`tsc --noEmit`)
- [x] No backend or frontend changes

## What this phase intentionally does NOT do

- **Run the tests from this session.** The tests need a live AppHost, and the execution model is operator-driven (dev runs AppHost in one terminal, tests in another). The repository provides the suite; the operator brings the runtime.
- **Wire into CI.** A GitHub Actions job that spins up the AppHost + runs Playwright is a separate deployment-hardening task. Deferred.
- **Pre-authenticate via cookie injection.** Real UI flows are slower but catch more regressions. Login happens through the form on every test.
- **Test dark mode / theme toggle.** Deferred — the theme is a visual concern, not a behavior.
- **Video recording on failure.** `trace: 'on-first-retry'` + `screenshot: 'only-on-failure'` are enough for MVP CI diagnostics.
