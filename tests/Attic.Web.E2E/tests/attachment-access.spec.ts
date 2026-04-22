import { test, expect, BrowserContext } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PIXEL_PATH = path.join(__dirname, '..', 'fixtures', 'fixtures', 'pixel.png');

test('upload image → member can download, non-member gets 403', async ({ browser, request }) => {
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  await registerFreshUser(pageOwner);

  const roomName = `att-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', roomName);

  // Attach the pixel via the hidden file input.
  const fileInput = pageOwner.locator('input[type="file"]').first();
  await fileInput.setInputFiles(PIXEL_PATH);

  // Wait for the upload chip to show "done" (i.e., no uploading spinner).
  await expect(pageOwner.getByText(/pixel\.png/i)).toBeVisible({ timeout: 10_000 });

  // Send the message carrying the attachment.
  await sendMessage(pageOwner, 'here is a pixel');

  // Grab the attachment URL via a <img> element bound to /api/attachments/<id>.
  const attachmentImg = pageOwner.locator('img[src*="/api/attachments/"]').first();
  await expect(attachmentImg).toBeVisible({ timeout: 10_000 });
  const attachmentSrc = await attachmentImg.getAttribute('src');
  if (!attachmentSrc) throw new Error('Missing attachment src');

  // Owner (current session) can fetch the bytes.
  const ownerCookies = await ctxOwner.cookies();
  const cookieHeader = ownerCookies.map(c => `${c.name}=${c.value}`).join('; ');
  const okResp = await request.get(attachmentSrc, { headers: { Cookie: cookieHeader } });
  expect(okResp.status()).toBe(200);

  // Non-member registers in a fresh context and is denied.
  const ctxOther = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOther = await ctxOther.newPage();
  await registerFreshUser(pageOther);
  const otherCookies = await ctxOther.cookies();
  const otherHeader = otherCookies.map(c => `${c.name}=${c.value}`).join('; ');
  const forbidden = await request.get(attachmentSrc, { headers: { Cookie: otherHeader } });
  expect(forbidden.status()).toBe(403);

  await ctxOwner.close();
  await ctxOther.close();
});
