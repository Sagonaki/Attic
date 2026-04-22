import { test, expect } from '../fixtures/test';

test('unauthenticated visit to a protected route redirects to /login', async ({ page }) => {
  // Clear any prior session state.
  await page.context().clearCookies();

  // Try several protected routes. Each should end up on /login.
  for (const route of ['/', '/contacts', '/profile', '/settings/sessions', '/invitations']) {
    await page.goto(route);
    await expect(page).toHaveURL(/\/login(\?.*)?$/, { timeout: 10_000 });
  }
});
