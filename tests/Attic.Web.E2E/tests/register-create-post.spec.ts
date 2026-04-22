import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('register → create room → send message → reload → message persists', async ({ page }) => {
  const user = await registerFreshUser(page);
  const roomName = `room-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', roomName, 'E2E golden-path');

  const content = `hello from ${user.username}`;
  await sendMessage(page, content);

  // Reload the page; the message should still be there.
  await page.reload();
  await expect(page.getByText(content)).toBeVisible({ timeout: 10_000 });
});
