import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';

test('B declines A\'s friend request → A\'s outgoing list drops it, no friendship', async ({ browser }) => {
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  const pageB = await ctxB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // A sends.
  await pageA.goto('/contacts');
  await pageA.getByRole('button', { name: /send friend request/i }).click();
  const dlg = pageA.getByRole('dialog');
  await dlg.getByPlaceholder(/username/i).fill(userB.username);
  await dlg.getByRole('button', { name: userB.username }).click();
  await dlg.getByRole('button', { name: /^send$/i }).click();
  await expect(dlg).not.toBeVisible({ timeout: 10_000 });

  // B declines.
  await pageB.goto('/contacts');
  await pageB.getByRole('tab', { name: /incoming/i }).click();
  await pageB.getByRole('button', { name: /^decline$/i }).first().click();

  // B sees no incoming requests.
  await expect(pageB.getByText(/no incoming requests/i)).toBeVisible({ timeout: 10_000 });

  // A is NOT friends with B: friends tab empty.
  await pageA.goto('/contacts');
  await pageA.getByRole('tab', { name: /friends/i }).click();
  await expect(pageA.getByText(userB.username)).toHaveCount(0);
  await expect(pageA.getByText(/no friends yet/i)).toBeVisible();

  // User A check the outgoing tab (also empty, the request was declined, not stuck pending).
  await pageA.getByRole('tab', { name: /outgoing/i }).click();
  await expect(pageA.getByText(userB.username)).toHaveCount(0);

  await ctxA.close();
  await ctxB.close();
  // Suppress unused-variable warning — userA is captured for future assertions
  // but not needed here; keep to document who A is.
  void userA;
});
