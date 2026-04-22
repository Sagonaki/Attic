import { Page, expect } from '@playwright/test';

/** Opens the "New room" modal, fills it, submits. Navigates to /chat/<id> on success. */
export async function createChannel(
  page: Page,
  kind: 'public' | 'private',
  name: string,
  description?: string
): Promise<void> {
  await page.getByRole('button', { name: /new room/i }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByPlaceholder(/Name \(3-120/).fill(name);
  if (description) await dialog.getByPlaceholder(/Description/).fill(description);
  if (kind === 'private') {
    await dialog.getByLabel(/private/i).click();
  }
  await dialog.getByRole('button', { name: /^create$/i }).click();
  // On success the modal closes and the URL becomes /chat/<id>.
  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i, { timeout: 10_000 });
}

/** Sends a message through the composer. */
export async function sendMessage(page: Page, text: string): Promise<void> {
  const composer = page.getByPlaceholder(/Type a message/i);
  await composer.fill(text);
  await page.getByRole('button', { name: /send message/i }).click();
  await expect(page.getByText(text).last()).toBeVisible({ timeout: 10_000 });
}
