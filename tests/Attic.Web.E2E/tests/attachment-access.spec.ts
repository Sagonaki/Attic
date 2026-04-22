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

  // Wait for the upload chip to render AND transition into 'done' state.
  // The hook's `readyAttachments` is computed from `status === 'done' && attachment`,
  // so we must not click Send before React has committed that state. Polling
  // the chip's text content until it matches just the file name (no "…",
  // no "!") is the reliable gate.
  await expect(pageOwner.getByText(/pixel\.png/i)).toBeVisible({ timeout: 10_000 });
  await expect.poll(
    async () => {
      const chipText = (await pageOwner.getByText(/pixel\.png/i).textContent()) ?? '';
      return { done: !chipText.includes('…') && !chipText.includes('!'), text: chipText };
    },
    { timeout: 10_000, intervals: [200, 500] }
  ).toMatchObject({ done: true });
  // Extra beat so the functional `setPending(prev => prev.map(...))` commit flushes.
  await pageOwner.waitForTimeout(300);

  // Send the message carrying the attachment.
  await sendMessage(pageOwner, 'here is a pixel');

  // Give the chat window a beat so the MessageCreated broadcast lands and
  // React commits the AttachmentPreview render before we assert on the anchor.
  await pageOwner.waitForTimeout(500);

  // Grab the attachment URL from the anchor the AttachmentPreview renders.
  // Both image and file variants render `<a href={downloadUrl}>`; looking for
  // the anchor instead of `<img>` lets this test cover both.
  const attachmentLink = pageOwner.locator('a[href*="/api/attachments/"]').first();
  await expect(attachmentLink).toBeVisible({ timeout: 15_000 });
  const attachmentSrc = await attachmentLink.getAttribute('href');
  if (!attachmentSrc) throw new Error('Missing attachment href');

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
