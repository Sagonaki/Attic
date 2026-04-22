import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('A invites B to private, B accepts, A sends, B sees in realtime', async ({ browser }) => {
  // Two independent browser contexts (separate cookie jars = separate users).
  const contextA: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const contextB: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageA = await contextA.newPage();
  const pageB = await contextB.newPage();

  const userA = await registerFreshUser(pageA);
  const userB = await registerFreshUser(pageB);

  // A creates a private room.
  const roomName = `priv-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageA, 'private', roomName);

  // A opens the RoomDetails panel (right side) and invites B.
  await pageA.getByRole('button', { name: /invite user/i }).click();
  const dialogA = pageA.getByRole('dialog', { name: /invite to room/i });
  await expect(dialogA).toBeVisible();
  await dialogA.getByPlaceholder(/Search by username/i).fill(userB.username);
  await dialogA.getByRole('button', { name: userB.username }).click();   // pick from results
  await dialogA.getByRole('button', { name: /^send$/i }).click();
  await expect(dialogA).not.toBeVisible({ timeout: 10_000 });

  // B navigates to /invitations and accepts.
  await pageB.goto('/invitations');
  await pageB.getByRole('button', { name: /^accept$/i }).first().click();
  // B is now in the room — the sidebar's Private tab should list it.
  await pageB.getByRole('button', { name: /private/i }).click();
  await expect(pageB.getByRole('link', { name: new RegExp(roomName) })).toBeVisible({ timeout: 15_000 });

  // A sends a message.
  await pageA.bringToFront();
  const msg = `realtime-${Date.now()}`;
  await sendMessage(pageA, msg);

  // B navigates to the room and should see the message arriving via SignalR.
  await pageB.bringToFront();
  await pageB.getByRole('link', { name: new RegExp(roomName) }).click();
  await expect(pageB.getByText(msg)).toBeVisible({ timeout: 15_000 });

  await contextA.close();
  await contextB.close();

  // Suppress unused variable warning — userA was used for registration side-effect.
  void userA;
});
