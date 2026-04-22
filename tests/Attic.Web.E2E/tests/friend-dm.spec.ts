import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { sendMessage } from '../fixtures/channels';

test('friend request → accept → open DM → message delivers', async ({ browser }) => {
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  const pageB = await ctxB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // A sends a friend request to B via the Contacts modal.
  await pageA.goto('/contacts');
  await pageA.getByRole('button', { name: /send friend request/i }).click();
  const dialog = pageA.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByPlaceholder(/username/i).fill(userB.username);
  // The modal shows matching users as buttons; pick the exact username.
  await dialog.getByRole('button', { name: userB.username }).click();
  await dialog.getByRole('button', { name: /^send$/i }).click();
  await expect(dialog).not.toBeVisible({ timeout: 10_000 });

  // B goes to /contacts → Incoming tab → accepts.
  await pageB.goto('/contacts');
  await pageB.getByRole('tab', { name: /incoming/i }).click();
  await pageB.getByRole('button', { name: /^accept$/i }).first().click();

  // B now sees A in the friends list.
  await pageB.getByRole('tab', { name: /friends/i }).click();
  await expect(pageB.getByText(userA.username)).toBeVisible({ timeout: 10_000 });

  // B opens a chat with A via the per-friend Chat button.
  await pageB.getByRole('button', { name: /^chat$/i }).first().click();
  await expect(pageB).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i, { timeout: 10_000 });

  // Personal chat works — message send + render.
  const hello = `dm hello ${Date.now()}`;
  await sendMessage(pageB, hello);
  await expect(pageB.getByText(hello)).toBeVisible();

  await ctxA.close();
  await ctxB.close();
});
