import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';

test('theme toggle switches classes and persists across reload', async ({ page }) => {
  await registerFreshUser(page);

  // Theme is stored at localStorage['attic.theme'] and applied as a class on <html>.
  // Read initial state (default is system → resolvedTheme = 'light' in headed Chromium).
  const initialClass = await page.evaluate(() => document.documentElement.className);
  // On fresh sessions the default is either 'light' or 'dark' depending on OS prefers-color-scheme.
  expect(initialClass).toMatch(/light|dark/);

  // The toggle cycles system → light → dark → system. The first click may or
  // may not change the DOM class depending on the OS prefers-color-scheme
  // resolution: if the OS is "light" and we're on "system", the first click
  // picks "light" explicitly and the class stays "light". Click until the
  // class actually changes (max 2 clicks through the cycle).
  const themeButton = page.getByRole('button', { name: /click for /i });
  await themeButton.click();
  let classNow = await page.evaluate(() => document.documentElement.className);
  if (classNow === initialClass) {
    await themeButton.click();
    classNow = await page.evaluate(() => document.documentElement.className);
  }
  expect(classNow).not.toBe(initialClass);

  // Persistence: reload and confirm the class is still what we toggled to.
  const beforeReload = classNow;
  await page.reload();
  await page.waitForLoadState('domcontentloaded');
  const afterReload = await page.evaluate(() => document.documentElement.className);
  expect(afterReload).toBe(beforeReload);

  // Sanity: localStorage also survives.
  const stored = await page.evaluate(() => window.localStorage.getItem('attic.theme'));
  expect(stored).toMatch(/light|dark|system/);
});
