import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('non-owner member leaves a public room → disappears from sidebar', async ({ browser }) => {
  // Owner creates a public room.
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  await registerFreshUser(pageOwner);
  const roomName = `leave-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', roomName);
  await ctxOwner.close();

  // Member registers, joins via catalog, then leaves.
  const ctxMember: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageMember = await ctxMember.newPage();
  await registerFreshUser(pageMember);

  await pageMember.goto('/catalog');
  await pageMember.getByPlaceholder(/Search by name prefix/i).fill(roomName);
  await expect(pageMember.getByText(roomName, { exact: true })).toBeVisible({ timeout: 10_000 });
  await pageMember.getByRole('button', { name: /^join$/i }).first().click();
  await expect(pageMember).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i);

  // RoomDetails panel → Leave room.
  await pageMember.getByRole('button', { name: /leave room/i }).click();
  // Modal confirmation.
  const dialog = pageMember.getByRole('dialog');
  if (await dialog.isVisible().catch(() => false)) {
    await dialog.getByRole('button', { name: /leave|confirm|yes/i }).click();
  }

  // The room no longer appears in the sidebar under public.
  await expect(pageMember.getByRole('link', { name: new RegExp(roomName) })).toHaveCount(0, { timeout: 10_000 });

  await ctxMember.close();
});
