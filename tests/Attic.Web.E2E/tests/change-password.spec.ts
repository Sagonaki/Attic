import { test, expect } from '../fixtures/test';
import { registerFreshUser, login, logout, openProfileMenu } from '../fixtures/users';

test('change password via /profile → logout → login with the new password', async ({ page }) => {
  const user = await registerFreshUser(page);
  const newPassword = 'hunter3pw-new';

  await openProfileMenu(page, user.username);
  await page.getByRole('menuitem', { name: /my profile/i }).click();
  await expect(page).toHaveURL(/\/profile$/);

  await page.getByPlaceholder('Current password').fill(user.password);
  await page.getByPlaceholder(/^New password/).fill(newPassword);
  await page.getByPlaceholder('Confirm new password').fill(newPassword);
  await page.getByRole('button', { name: /update password/i }).click();

  // The form accepts: either a success toast appears OR the fields clear.
  // Give the request a beat to complete before logging out.
  await page.waitForTimeout(1_000);

  // Log out and log back in with the NEW password.
  await logout(page, user.username);
  await login(page, user.email, newPassword);
  await expect(page.getByRole('button', { name: new RegExp(user.username) })).toBeVisible();
});
