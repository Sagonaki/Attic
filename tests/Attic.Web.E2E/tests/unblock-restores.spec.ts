import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';

test('unblock removes the block row but does NOT restore the friendship', async ({ browser }) => {
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  const pageB = await ctxB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // Friend dance: A → B, B accepts.
  await pageA.goto('/contacts');
  await pageA.getByRole('button', { name: /send friend request/i }).click();
  const dlg = pageA.getByRole('dialog');
  await dlg.getByPlaceholder(/username/i).fill(userB.username);
  await dlg.getByRole('button', { name: userB.username }).click();
  await dlg.getByRole('button', { name: /^send$/i }).click();
  await expect(dlg).not.toBeVisible({ timeout: 10_000 });

  await pageB.goto('/contacts');
  await pageB.getByRole('tab', { name: /incoming/i }).click();
  await pageB.getByRole('button', { name: /^accept$/i }).first().click();
  await pageB.getByRole('tab', { name: /friends/i }).click();
  await expect(pageB.getByText(userA.username)).toBeVisible({ timeout: 10_000 });

  // B blocks A.
  const friendRow = pageB.getByRole('listitem').filter({ hasText: userA.username });
  await friendRow.getByRole('button').last().click();
  await pageB.getByRole('menuitem', { name: /block user/i }).click();

  // Switch to Blocked tab — A should be listed.
  await pageB.getByRole('tab', { name: /blocked/i }).click();
  await expect(pageB.getByText(userA.username)).toBeVisible({ timeout: 10_000 });

  // B unblocks A.
  await pageB.getByRole('button', { name: /unblock/i }).first().click();

  // Blocked list is empty.
  await expect(pageB.getByText(/no blocked|nobody blocked|empty/i)).toBeVisible({ timeout: 10_000 });

  // But the friendship is NOT auto-restored — friends tab stays empty.
  await pageB.getByRole('tab', { name: /friends/i }).click();
  await expect(pageB.getByText(userA.username)).toHaveCount(0);
  await expect(pageB.getByText(/no friends yet/i)).toBeVisible();

  await ctxA.close();
  await ctxB.close();
});
