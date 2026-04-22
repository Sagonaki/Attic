import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage, messageAction, messageRow } from '../fixtures/channels';

test('sender can edit their own message — (edited) marker renders', async ({ page }) => {
  await registerFreshUser(page);
  const room = `edit-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const original = `original content ${Date.now()}`;
  await sendMessage(page, original);

  await messageAction(page, original, 'Edit');

  // In edit mode the row's rendered text is replaced by an <input> whose value
  // sits in the `value` attribute (not in the DOM text). The row can't be found
  // by "has-text(original)" anymore. The Save button is unique while any row is
  // being edited, so we pivot on it: input is the sibling textbox, row is the
  // ancestor .group container.
  const saveBtn = page.getByRole('button', { name: /^save$/i });
  await expect(saveBtn).toBeVisible({ timeout: 10_000 });
  const input = saveBtn.locator('xpath=../input');
  await input.fill('');
  const edited = `edited content ${Date.now()}`;
  await input.fill(edited);
  await saveBtn.click();

  // New content is visible, the `(edited)` marker is rendered on that row.
  await expect(page.getByText(edited)).toBeVisible({ timeout: 10_000 });
  await expect(messageRow(page, edited).getByText(/\(edited\)/i)).toBeVisible();
});
