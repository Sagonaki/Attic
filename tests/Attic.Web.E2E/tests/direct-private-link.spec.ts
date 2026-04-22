import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('non-member navigating directly to a private channel URL sees 404 behavior (no leak)', async ({ browser }) => {
  // Owner creates a private room → capture its UUID from the URL.
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  await registerFreshUser(pageOwner);
  const roomName = `priv-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'private', roomName);

  const url = pageOwner.url();
  const channelId = url.match(/\/chat\/([0-9a-f-]{36})/)?.[1];
  if (!channelId) throw new Error('Could not capture channel id from owner URL');
  await ctxOwner.close();

  // Outsider registers in a fresh context and navigates directly to the private URL.
  const ctxOut: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOut = await ctxOut.newPage();
  await registerFreshUser(pageOut);
  await pageOut.goto(`/chat/${channelId}`);

  // Core enumeration-fix assertion: the private room's name must NOT leak to
  // the outsider. The API returns 404 for non-members of private channels
  // (commit d6b30a8) — the SPA renders an empty/error state for the unknown
  // channel, but the room's identity stays hidden. The SPA also doesn't list
  // the channel in the sidebar's private tab for this user.
  await pageOut.waitForTimeout(1_500);
  await expect(pageOut.getByText(roomName)).toHaveCount(0);
  await pageOut.getByRole('tab', { name: /private/i }).click();
  await expect(pageOut.getByRole('link', { name: new RegExp(roomName) })).toHaveCount(0);

  await ctxOut.close();
});
