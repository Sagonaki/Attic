import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, openRoomByName, sendMessage } from '../fixtures/channels';

test('browse public catalog → join a room created by another user → post', async ({ browser }) => {
  // User A creates a public room so there's something for user B to join.
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  await registerFreshUser(pageA);
  const roomName = `cat-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageA, 'public', roomName, 'join-from-catalog scenario');
  await ctxA.close();

  // User B navigates to the catalog and joins.
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageB = await ctxB.newPage();
  await registerFreshUser(pageB);

  await pageB.goto('/catalog');
  const searchBox = pageB.getByPlaceholder(/Search by name prefix/i);
  await searchBox.fill(roomName);

  // Catalog rows are <div>s (not <li>s) — the search narrows the list to this
  // single room, so the sole visible Join button belongs to it.
  await expect(pageB.getByText(roomName, { exact: true })).toBeVisible({ timeout: 10_000 });
  await pageB.getByRole('button', { name: /^join$/i }).first().click();

  // Sidebar now lists the room under public; posting works.
  await openRoomByName(pageB, roomName);
  const msg = `joined-via-catalog ${Date.now()}`;
  await sendMessage(pageB, msg);
  await expect(pageB.getByText(msg)).toBeVisible();

  await ctxB.close();
});
