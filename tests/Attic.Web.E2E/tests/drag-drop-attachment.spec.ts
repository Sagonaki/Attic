import { test, expect } from '../fixtures/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PIXEL_PATH = path.resolve(__dirname, '..', 'fixtures', 'fixtures', 'pixel.png');

test('drop a file onto the composer → uploads and attaches', async ({ page }) => {
  await registerFreshUser(page);
  const room = `drop-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const pixelBytes = fs.readFileSync(PIXEL_PATH);
  const base64 = pixelBytes.toString('base64');

  // Simulate the full drag-and-drop pipeline onto the ChatInput's outer div,
  // which handles `onDrop` (see ChatInput.tsx `onDrop` handler).
  await page.evaluate(async ({ base64 }) => {
    const blob = await (await fetch(`data:image/png;base64,${base64}`)).blob();
    const file = new File([blob], 'dropped.png', { type: 'image/png' });
    const dt = new DataTransfer();
    dt.items.add(file);
    // The drop handler lives on the container div that wraps the composer.
    const target = document.querySelector<HTMLElement>('textarea[placeholder="Type a message…"]')
      ?.closest('div[class*="border-t"]')?.parentElement;   // wrapper w/ onDragOver+onDrop
    if (!target) throw new Error('drop target not found');
    target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
    target.dispatchEvent(new DragEvent('drop',     { bubbles: true, cancelable: true, dataTransfer: dt }));
  }, { base64 });

  await expect(page.getByText(/dropped\.png/i)).toBeVisible({ timeout: 10_000 });

  await sendMessage(page, 'here is a dropped pixel');
  await page.waitForTimeout(500);
  await expect(page.locator('a[href*="/api/attachments/"]').first()).toBeVisible({ timeout: 10_000 });
});
