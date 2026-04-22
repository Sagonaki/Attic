import { test, expect } from '../fixtures/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('uploading a non-image file renders the file-style preview (not an <img>)', async ({ page }) => {
  await registerFreshUser(page);
  const room = `file-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  // Write a small text fixture to a tmp path (keeps the repo free of binaries).
  const tmpFile = path.join(os.tmpdir(), `attic-e2e-${Date.now()}.txt`);
  fs.writeFileSync(tmpFile, 'hello from the E2E suite\n');

  await page.locator('input[type="file"]').first().setInputFiles(tmpFile);
  await expect(page.getByText(new RegExp(path.basename(tmpFile)))).toBeVisible({ timeout: 10_000 });

  // Wait for upload completion via the chip's loading marker clearing.
  await expect.poll(
    async () => {
      const chipText = (await page.getByText(new RegExp(path.basename(tmpFile))).textContent()) ?? '';
      return !chipText.includes('…') && !chipText.includes('!');
    },
    { timeout: 10_000, intervals: [200, 500] }
  ).toBe(true);
  await page.waitForTimeout(300);

  await sendMessage(page, 'here is a text file');
  await page.waitForTimeout(500);

  // The non-image branch of AttachmentPreview renders an <a> anchor with the
  // filename inside — NOT an <img>. Assert both.
  const link = page.locator(`a[href*="/api/attachments/"]:has-text("${path.basename(tmpFile)}")`);
  await expect(link).toBeVisible({ timeout: 10_000 });
  // No <img> pointing to /api/attachments/ for this message (text file,
  // not rendered inline).
  await expect(page.locator('img[src*="/api/attachments/"]')).toHaveCount(0);

  fs.unlinkSync(tmpFile);
});
