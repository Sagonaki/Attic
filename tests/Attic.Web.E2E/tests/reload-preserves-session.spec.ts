import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';

test('reloading the page preserves the session (cookie auth survives)', async ({ page }) => {
  const user = await registerFreshUser(page);
  // Header shows the username; it's our check that the app is in the logged-in shell.
  await expect(page.getByRole('button', { name: new RegExp(user.username) })).toBeVisible();

  // Reload → cookie is still attached, /api/auth/me returns the user, shell re-hydrates.
  await page.reload();
  await expect(page.getByRole('button', { name: new RegExp(user.username) })).toBeVisible({ timeout: 10_000 });

  // URL stays on the shell (/), not redirected to /login.
  await expect(page).toHaveURL(/\/$|\/chat\//);
});
