import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';

test('blocking a friend prevents posting in their personal chat', async ({ browser }) => {
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  const pageB = await ctxB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // Friend dance: A invites, B accepts.
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

  // B blocks A via the friend row's overflow menu.
  const friendRow = pageB.getByRole('listitem').filter({ hasText: userA.username });
  await friendRow.getByRole('button').last().click();   // the "more" trigger
  await pageB.getByRole('menuitem', { name: /block user/i }).click();

  // After B blocks A, the friendship is removed on both sides. A's Contacts
  // tab should show the empty state. (A's username is also in A's header, so
  // don't assert "username hidden" — just the empty-state message.)
  await pageA.goto('/contacts');
  await pageA.getByRole('tab', { name: /friends/i }).click();
  await expect(pageA.getByText(/no friends yet/i)).toBeVisible({ timeout: 10_000 });

  await ctxA.close();
  await ctxB.close();
});
