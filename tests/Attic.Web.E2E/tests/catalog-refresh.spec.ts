import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('a public room created by one user is listed in /catalog for another user', async ({ browser }) => {
  // Explorer registers first and lands on the catalog.
  const ctxExplorer: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageExplorer = await ctxExplorer.newPage();
  await registerFreshUser(pageExplorer);
  await pageExplorer.goto('/catalog');

  // Creator makes a new public room with a distinctive name.
  const ctxCreator: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageCreator = await ctxCreator.newPage();
  await registerFreshUser(pageCreator);
  const roomName = `cf-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageCreator, 'public', roomName, 'visible in the catalog');

  // Explorer refreshes the catalog — the new public room is visible.
  // (The catalog is paginated + query-cached, so refresh is the simplest
  // way to assert server-side visibility without depending on realtime
  // invalidation for the catalog route, which isn't wired today.)
  await pageExplorer.reload();
  await pageExplorer.getByPlaceholder(/Search by name prefix/i).fill(roomName);
  await expect(pageExplorer.getByText(roomName, { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(pageExplorer.getByText('visible in the catalog')).toBeVisible();

  await ctxExplorer.close();
  await ctxCreator.close();
});
