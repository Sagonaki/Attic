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

/** Locates a message row by its visible content. Targets the outer `.group` div
 * so the actions menu (only visible on hover) is available. */
export function messageRow(page: Page, text: string) {
  // The row wraps the text in a `<div>` whose parent is `div.group`. Walk up
  // until we hit the group-class container.
  return page.locator(`div.group:has-text("${text}")`).last();
}

/** Opens the per-message actions dropdown and clicks the given item. */
export async function messageAction(
  page: Page,
  messageText: string,
  action: 'Edit' | 'Reply' | 'Delete'
): Promise<void> {
  const row = messageRow(page, messageText);
  // Hover to reveal the actions trigger (CSS: opacity-0 → opacity-100 on group-hover).
  await row.hover();
  await row.getByRole('button', { name: /message actions/i }).click();
  await page.getByRole('menuitem', { name: new RegExp(`^${action}$`, 'i') }).click();
}

/** Opens a room by its visible name in the sidebar. The link's accessible name
 * may include a trailing unread-count badge ("room-foo 3"), so we match
 * "name" + optional trailing digits instead of requiring an exact name. */
export async function openRoomByName(page: Page, name: string): Promise<void> {
  await page.getByRole('link', { name: new RegExp(`^${name}(\\s+[0-9]+)?$`) }).click();
  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i);
}
