import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage, messageAction, messageRow } from '../fixtures/channels';

test('sender can delete their own message — disappears from the list', async ({ page }) => {
  await registerFreshUser(page);
  const room = `del-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const keep = `keeper ${Date.now()}`;
  const doomed = `doomed ${Date.now()}`;
  await sendMessage(page, keep);
  // Small beat so the optimistic composer clear + re-render completes
  // before the next fill (back-to-back sends race otherwise).
  await page.waitForTimeout(250);
  await sendMessage(page, doomed);

  // Both visible before delete.
  await expect(page.getByText(keep)).toBeVisible();
  await expect(page.getByText(doomed)).toBeVisible();

  await messageAction(page, doomed, 'Delete');

  // The doomed row is gone; the kept one still renders.
  await expect(messageRow(page, doomed)).toHaveCount(0, { timeout: 10_000 });
  await expect(page.getByText(keep)).toBeVisible();
});
