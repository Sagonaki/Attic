import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

// Regression guard for the emoji-popover `left-0` positioning fix (commit 8eb752a):
// before the fix, the 352 px picker was absolute-positioned with right-0 anchored
// to the Smile button near the left edge of the chat input. The MAIN's
// overflow:hidden clipped ~300 px of the picker so real user clicks didn't
// hit-test any tile. This test verifies that (a) the picker renders inside
// MAIN's bounds and (b) a click on an emoji tile actually inserts it.
test('emoji picker: clicking a tile inserts the emoji into the composer', async ({ page }) => {
  await registerFreshUser(page);
  const room = `emoji-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const composer = page.getByPlaceholder(/Type a message/i);
  await composer.click();

  await page.getByRole('button', { name: /add emoji/i }).click();
  // Wait for the <em-emoji-picker> web component to mount.
  await page.waitForSelector('em-emoji-picker');

  // Click a specific tile via the shadow-piercing helper. The first `.flex-center.flex-middle`
  // button inside the shadow DOM is an emoji tile (aria-label is the unicode character).
  const result = await page.evaluate(() => {
    const host = document.querySelector('em-emoji-picker');
    const shadow = host?.shadowRoot;
    if (!shadow) return { inserted: false, reason: 'no shadow root' };
    const tile = shadow.querySelector<HTMLButtonElement>('button.flex.flex-center.flex-middle');
    if (!tile) return { inserted: false, reason: 'no tile' };
    const emoji = tile.getAttribute('aria-label');
    tile.click();
    return { inserted: true, emoji };
  });
  expect(result.inserted).toBe(true);

  // The picker should close after selection and the composer should contain the emoji.
  await expect(page.locator('em-emoji-picker')).toHaveCount(0, { timeout: 5_000 });
  const value = await composer.inputValue();
  expect(value.length).toBeGreaterThan(0);
  expect(value).toBe(result.emoji!);
});
