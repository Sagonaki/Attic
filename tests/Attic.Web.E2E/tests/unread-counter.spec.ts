import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage, openRoomByName } from '../fixtures/channels';

test('unread badge appears when a peer sends; MessageCreated fan-out reaches every member', async ({ browser }) => {
  // Owner + member share a public room.
  const ctxOwner: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const ctxMember: BrowserContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const pageOwner = await ctxOwner.newPage();
  const pageMember = await ctxMember.newPage();

  await registerFreshUser(pageOwner);
  await registerFreshUser(pageMember);

  const room = `unread-${Date.now().toString(36)}`.slice(0, 20);
  await createChannel(pageOwner, 'public', room);

  // Member joins via catalog so they're subscribed for UnreadChanged broadcasts.
  await pageMember.goto('/catalog');
  await pageMember.getByPlaceholder(/Search by name prefix/i).fill(room);
  await expect(pageMember.getByText(room, { exact: true })).toBeVisible({ timeout: 10_000 });
  await pageMember.getByRole('button', { name: /^join$/i }).first().click();

  // Member navigates AWAY from the room so incoming messages accrue as unread.
  // The sidebar (with unread badges) stays mounted on every ChatShell route.
  await pageMember.goto('/contacts');
  // Give the hub a beat to finish handshake + OnConnectedAsync user-group add
  // before the owner fires the fan-out broadcasts.
  await pageMember.waitForTimeout(1_000);

  // Owner sends two messages in the room.
  await openRoomByName(pageOwner, room);
  const composer = pageOwner.getByPlaceholder(/Type a message/i);
  await expect(composer).toBeVisible();
  await sendMessage(pageOwner, 'unread test A');
  await pageOwner.waitForTimeout(250);
  await sendMessage(pageOwner, 'unread test B');

  // Member's sidebar badge should go from empty → some positive number. The
  // exact count depends on timing/dedupe semantics (StrictMode dev-mode can
  // double-fire effect subscriptions); we assert the contract — "a badge
  // appears after the owner sends" — not the specific integer.
  const roomLink = pageMember.getByRole('link', { name: new RegExp(room) });
  await expect
    .poll(async () => (await roomLink.textContent() ?? '').match(/[0-9]+/)?.[0] ?? '',
          { timeout: 15_000, intervals: [500, 1_000] })
    .toMatch(/^[1-9][0-9]*$/);

  // Member opens the room — confirm the fan-out delivered the messages over
  // SignalR's backplane (not just the unread count). This exercises the
  // MessageCreated broadcast end-to-end: hub persists → MessageFanoutService
  // drains → Clients.Group(Channel_<id>).SendAsync → member's subscription
  // adds the row to the messages query cache → ChatWindow renders it.
  await openRoomByName(pageMember, room);
  await expect(pageMember.getByText('unread test A')).toBeVisible({ timeout: 10_000 });
  await expect(pageMember.getByText('unread test B')).toBeVisible({ timeout: 10_000 });

  await ctxOwner.close();
  await ctxMember.close();
});
