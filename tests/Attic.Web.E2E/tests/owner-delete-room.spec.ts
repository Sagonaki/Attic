import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('owner deletes their public room → the room is gone from the sidebar', async ({ page }) => {
  await registerFreshUser(page);
  const room = `kill-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  // RoomDetails panel → Delete room.
  await page.getByRole('button', { name: /delete room/i }).click();

  // A confirmation dialog appears — accept it.
  const dialog = page.getByRole('dialog');
  if (await dialog.isVisible().catch(() => false)) {
    await dialog.getByRole('button', { name: /delete|confirm|yes/i }).click();
  }

  // Sidebar no longer lists the room; the "main" surface shows the empty state.
  await expect(page.getByRole('link', { name: new RegExp(`^${room}`) }))
    .toHaveCount(0, { timeout: 10_000 });
});
