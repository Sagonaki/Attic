import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage, messageAction } from '../fixtures/channels';

test('owner can delete another member\'s message', async ({ browser }) => {
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxMember: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  const pageMember = await ctxMember.newPage();

  await registerFreshUser(pageOwner);
  await registerFreshUser(pageMember);

  const room = `mod-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', room);

  // Member joins + posts.
  await pageMember.goto('/catalog');
  await pageMember.getByPlaceholder(/Search by name prefix/i).fill(room);
  await pageMember.getByRole('button', { name: /^join$/i }).first().click();
  const memberMsg = `delete-me-please ${Date.now()}`;
  await sendMessage(pageMember, memberMsg);

  // Owner opens the same room and sees the member's message.
  await pageOwner.reload();
  await expect(pageOwner.getByText(memberMsg)).toBeVisible({ timeout: 10_000 });

  // Owner invokes Delete via the message actions menu (admin/owner is allowed
  // per AuthorizationRules.CanDeleteMessage). Note: the current isAdmin prop
  // hard-codes false in ChatWindow — if the menu doesn't show Delete, this
  // is a bug we'd want to report. Catch+flag explicitly.
  await messageAction(pageOwner, memberMsg, 'Delete').catch(async () => {
    // Fallback diagnostic: if the menu doesn't expose Delete to the owner,
    // surface a clear failure instead of a timeout.
    throw new Error(
      'Owner could not find Delete in the message actions menu — the SPA may not be ' +
      'propagating admin/owner state to MessageActionsMenu.isAdmin.'
    );
  });

  // The deleted message disappears from the owner's view.
  await expect(pageOwner.getByText(memberMsg)).toHaveCount(0, { timeout: 10_000 });

  await ctxOwner.close();
  await ctxMember.close();
});
