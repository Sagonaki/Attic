import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

test('second context sees the first context\'s message in realtime', async ({ browser }) => {
  // Owner creates a public room, member joins via catalog. Owner sends a
  // message; member's chat window renders it via MessageCreated broadcast.
  // (Two different users — simpler and more realistic than re-logging in the
  // same user across contexts and dealing with session/cookie duplication.)
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxMember: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  const pageMember = await ctxMember.newPage();

  await registerFreshUser(pageOwner);
  await registerFreshUser(pageMember);

  const room = `echo-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', room);

  await pageMember.goto('/catalog');
  await pageMember.getByPlaceholder(/Search by name prefix/i).fill(room);
  await expect(pageMember.getByText(room, { exact: true })).toBeVisible({ timeout: 10_000 });
  await pageMember.getByRole('button', { name: /^join$/i }).first().click();
  await expect(pageMember).toHaveURL(/\/chat\/[0-9a-f-]{36}$/i);

  // Short beat so member's hub handshake + SubscribeToChannel completes.
  await pageMember.waitForTimeout(800);

  // Owner sends.
  const msg = `realtime-echo ${Date.now()}`;
  await sendMessage(pageOwner, msg);

  // Member's chat window receives the broadcast.
  await expect(pageMember.getByText(msg)).toBeVisible({ timeout: 10_000 });

  await ctxOwner.close();
  await ctxMember.close();
});
