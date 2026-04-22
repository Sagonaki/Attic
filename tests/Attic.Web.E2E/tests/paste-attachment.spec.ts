import { test, expect } from '../fixtures/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PIXEL_PATH = path.resolve(__dirname, '..', 'fixtures', 'fixtures', 'pixel.png');

test('paste an image from the clipboard into the composer → uploads and attaches', async ({ page }) => {
  await registerFreshUser(page);
  const room = `paste-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  const composer = page.getByPlaceholder(/Type a message/i);
  await composer.click();

  // Read the pixel into the page as a File + dispatch a synthetic paste event
  // carrying it on clipboardData.files. ChatInput's onPaste hook reads
  // e.clipboardData.files and calls upload() when files are present.
  const pixelBytes = fs.readFileSync(PIXEL_PATH);
  const base64 = pixelBytes.toString('base64');

  await page.evaluate(async ({ base64 }) => {
    const blob = await (await fetch(`data:image/png;base64,${base64}`)).blob();
    const file = new File([blob], 'pasted.png', { type: 'image/png' });
    const dt = new DataTransfer();
    dt.items.add(file);
    const ta = document.querySelector<HTMLTextAreaElement>('textarea[placeholder="Type a message…"]')!;
    ta.focus();
    const evt = new ClipboardEvent('paste', {
      bubbles: true,
      cancelable: true,
      clipboardData: dt,
    });
    ta.dispatchEvent(evt);
  }, { base64 });

  // Chip appears with the pasted filename.
  await expect(page.getByText(/pasted\.png/i)).toBeVisible({ timeout: 10_000 });

  // Send with some text; the rendered message carries an attachment anchor.
  await sendMessage(page, 'here is a pasted pixel');
  await page.waitForTimeout(500);
  await expect(page.locator('a[href*="/api/attachments/"]').first()).toBeVisible({ timeout: 10_000 });
});
