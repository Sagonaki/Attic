import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';

test('send-friend-request modal: typing a prefix filters users to matches only', async ({ browser }) => {
  // Create a target user with a distinctive username prefix.
  const ctxTarget: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageTarget = await ctxTarget.newPage();
  const target = await registerFreshUser(pageTarget);
  await ctxTarget.close();

  // Fresh searcher registers, opens contacts → send-friend-request modal.
  const ctxSearch: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageSearch = await ctxSearch.newPage();
  await registerFreshUser(pageSearch);

  await pageSearch.goto('/contacts');
  await pageSearch.getByRole('button', { name: /send friend request/i }).click();
  const dlg = pageSearch.getByRole('dialog');
  await expect(dlg).toBeVisible();

  // Use a long prefix unique to the target — the server returns top-20
  // alphabetical prefix matches, so short prefixes get crowded out by other
  // test users registered earlier in the DB's lifetime.
  const prefix = target.username.slice(0, 10);
  await dlg.getByPlaceholder(/username/i).fill(prefix);

  // A button with the target's exact username should be available to click.
  await expect(dlg.getByRole('button', { name: target.username })).toBeVisible({ timeout: 10_000 });

  // A completely unrelated prefix yields zero matches (empty state or no buttons).
  await dlg.getByPlaceholder(/username/i).fill('zzzzzzneverexists');
  await expect(dlg.getByRole('button', { name: target.username })).toHaveCount(0, { timeout: 5_000 });

  await ctxSearch.close();
});
