import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage, messageAction, messageRow } from '../fixtures/channels';

test('reply-to shows the quoted original as context on the reply', async ({ page }) => {
  await registerFreshUser(page);
  const room = `reply-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const original = `the original ${Date.now()}`;
  await sendMessage(page, original);

  await messageAction(page, original, 'Reply');

  // The composer shows a reply preview bar referencing the original snippet.
  await expect(page.getByText(original).first()).toBeVisible();

  const reply = `this is the reply ${Date.now()}`;
  await sendMessage(page, reply);

  // The reply's row shows the quoted original (renders via the `replyToId` branch
  // in ChatWindow — the sender-username + snippet appear above the reply body).
  const replyRow = messageRow(page, reply);
  await expect(replyRow.getByText(original.slice(0, 80))).toBeVisible();
});
