import { Page, expect } from '@playwright/test';

export interface RegisteredUser {
  email: string;
  username: string;
  password: string;
}

/** Registers a fresh user through the UI register form. Returns the credentials. */
export async function registerFreshUser(page: Page): Promise<RegisteredUser> {
  const suffix = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`;
  const creds: RegisteredUser = {
    email: `e2e-${suffix}@example.test`,
    username: `e2e${suffix}`.slice(0, 20),
    password: 'hunter2pw',
  };
  await page.goto('/register');
  await page.getByPlaceholder('Email').fill(creds.email);
  await page.getByPlaceholder('Username').fill(creds.username);
  await page.getByPlaceholder('Password').fill(creds.password);
  await page.getByRole('button', { name: /register/i }).click();
  // After register, the app redirects to '/'; confirm the shell loaded.
  await expect(page.getByText(creds.username)).toBeVisible({ timeout: 10_000 });
  return creds;
}

/** Logs in an existing user. */
export async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByPlaceholder('Email').fill(email);
  await page.getByPlaceholder('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/$|\/chat\//);
}

/** Opens the header profile dropdown (avatar + username button). */
export async function openProfileMenu(page: Page, username: string): Promise<void> {
  await page.getByRole('button', { name: new RegExp(username) }).click();
}

/** Signs out via the profile dropdown. Returns once /login is visible. */
export async function logout(page: Page, username: string): Promise<void> {
  await openProfileMenu(page, username);
  await page.getByRole('menuitem', { name: /sign out/i }).click();
  await expect(page).toHaveURL(/\/login$/, { timeout: 10_000 });
}
