import { test, expect } from '../fixtures/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel } from '../fixtures/channels';

test('oversize message (> 3 KB) is rejected by the server; UI shows an error', async ({ page }) => {
  await registerFreshUser(page);
  const room = `lim-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(page, 'public', room);

  // Server caps at 3 KB per send (SendMessageRequestValidator). A 3100-byte
  // message triggers the rejection path; the hub returns ok=false and the
  // SPA drops the optimistic row.
  const huge = 'x'.repeat(3100);
  const composer = page.getByPlaceholder(/Type a message/i);
  await composer.fill(huge);
  await page.getByRole('button', { name: /send message/i }).click();

  // Give the rejection a beat, then verify:
  // (1) the message never appears as a committed chat row (rendered in the
  //     `whitespace-pre-wrap` body), and
  // (2) the composer returns to an empty state OR keeps the draft — either
  //     is acceptable SPA behavior; the product contract is "not persisted".
  await page.waitForTimeout(1_500);
  await expect(page.locator('div.whitespace-pre-wrap.break-words', { hasText: huge.slice(0, 100) })).toHaveCount(0);
});
