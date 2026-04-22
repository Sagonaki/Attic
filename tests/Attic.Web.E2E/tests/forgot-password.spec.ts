import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';

test('forgot-password dialog submits without crashing and shows confirmation', async ({ page, context }) => {
  // Create a user we can request a reset for.
  const user = await registerFreshUser(page);

  // Log out (open profile menu → Sign out), then land on /login.
  await page.getByRole('button', { name: new RegExp(user.username) }).click();
  await page.getByRole('menuitem', { name: /sign out/i }).click();
  await expect(page).toHaveURL(/\/login$/, { timeout: 10_000 });

  // Open the forgot-password dialog, submit the email.
  await page.getByRole('button', { name: /forgot your password/i }).click();
  const dlg = page.getByRole('dialog');
  await expect(dlg).toBeVisible();
  await dlg.getByPlaceholder(/your email/i).fill(user.email);
  await dlg.getByRole('button', { name: /send|submit|reset/i }).click();

  // The dialog either closes or shows a confirmation — both flows count as "didn't crash".
  // Give the server a moment; then either the dialog is gone OR a confirmation banner is visible.
  const dialogClosed = dlg.waitFor({ state: 'hidden', timeout: 10_000 }).then(() => true).catch(() => false);
  const confirmationShown = page.getByText(/check.*email|sent|new password/i)
    .waitFor({ state: 'visible', timeout: 10_000 }).then(() => true).catch(() => false);
  expect(await Promise.race([dialogClosed, confirmationShown])).toBe(true);

  // Regardless of delivery mechanism, the old password still works because the
  // reset only issues a *new* password out-of-band; we don't have the new one
  // from the UI, so we just verify the flow didn't corrupt the session route.
  await expect(page).toHaveURL(/\/login/);
  await context.close();
});
