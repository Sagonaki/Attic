import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('invitee declines a private-channel invitation → it disappears from inbox', async ({ browser }) => {
  const ctxA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await ctxA.newPage();
  const pageB = await ctxB.newPage();

  await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  const roomName = `inv-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageA, 'private', roomName);

  // A invites B via RoomDetails → Invite user.
  await pageA.getByRole('button', { name: /invite user/i }).click();
  const inviteDlg = pageA.getByRole('dialog', { name: /invite to room/i });
  await expect(inviteDlg).toBeVisible();
  await inviteDlg.getByPlaceholder(/Search by username/i).fill(userB.username);
  await inviteDlg.getByRole('button', { name: userB.username }).click();
  await inviteDlg.getByRole('button', { name: /^send$/i }).click();
  await expect(inviteDlg).not.toBeVisible({ timeout: 10_000 });

  // B goes to /invitations → declines.
  await pageB.goto('/invitations');
  await expect(pageB.getByText(roomName)).toBeVisible({ timeout: 10_000 });
  await pageB.getByRole('button', { name: /^decline$/i }).first().click();

  // The invitation row disappears.
  await expect(pageB.getByText(roomName)).toHaveCount(0, { timeout: 10_000 });

  // And the room does NOT appear in B's private sidebar.
  await pageB.goto('/');
  await pageB.getByRole('tab', { name: /private/i }).click();
  await expect(pageB.getByRole('link', { name: new RegExp(roomName) })).toHaveCount(0);

  await ctxA.close();
  await ctxB.close();
});
