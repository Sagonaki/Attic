import { test, expect } from '../fixtures/test';
import { registerFreshUser, openProfileMenu } from '../fixtures/users';

test('delete account → logged out → cannot log in with the tombstoned credentials', async ({ page }) => {
  const user = await registerFreshUser(page);

  await openProfileMenu(page, user.username);
  await page.getByRole('menuitem', { name: /delete account/i }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  // The modal requires the password as confirmation (AuthEndpoints.DeleteAccount).
  await dialog.getByPlaceholder(/password/i).fill(user.password);
  await dialog.getByRole('button', { name: /delete account/i }).click();

  // User is logged out → /login.
  await expect(page).toHaveURL(/\/login$/, { timeout: 10_000 });

  // Login attempt with the deleted account should fail with 401 (server
  // rejects soft-deleted users; the form shows an error banner).
  await page.getByPlaceholder('Email').fill(user.email);
  await page.getByPlaceholder('Password').fill(user.password);
  await page.getByRole('button', { name: /sign in/i }).click();

  // URL stays on /login (no navigation to the shell).
  await page.waitForTimeout(1_000);
  await expect(page).toHaveURL(/\/login$/);
});
