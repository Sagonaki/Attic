import { test, expect } from '../fixtures/test';
import { registerFreshUser, login, logout } from '../fixtures/users';

test('sign out → sign back in with the same credentials lands on the shell', async ({ page }) => {
  const user = await registerFreshUser(page);
  await logout(page, user.username);

  // Re-authenticate via the login form.
  await login(page, user.email, user.password);

  // Back in the app shell: the username is visible in the profile button.
  await expect(page.getByRole('button', { name: new RegExp(user.username) })).toBeVisible();
});
