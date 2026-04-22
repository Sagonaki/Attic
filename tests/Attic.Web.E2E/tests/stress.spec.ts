import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const CONTEXT_COUNT = Number(process.env.STRESS_CONTEXTS ?? 30);

test(`stress: ${CONTEXT_COUNT} parallel browser contexts run the golden path`, async ({ browser }) => {
  test.setTimeout(5 * 60 * 1000);   // 5 minutes — browser spin-up dominates at this scale.

  const contexts: BrowserContext[] = await Promise.all(
    Array.from({ length: CONTEXT_COUNT }, () => browser.newContext({ ignoreHTTPSErrors: true }))
  );

  try
  {
    // Each context: register + create own public room + send 5 messages + reload + verify last.
    await Promise.all(contexts.map(async (ctx, i) => {
      const page = await ctx.newPage();
      const user = await registerFreshUser(page);
      const roomName = `stress-${Date.now().toString(36)}-${i}`.slice(0, 20);
      await createChannel(page, 'public', roomName);

      for (let n = 1; n <= 5; n++)
      {
        await sendMessage(page, `msg ${n} from ${user.username}`);
      }

      await page.reload();
      await expect(page.getByText(`msg 5 from ${user.username}`)).toBeVisible({ timeout: 15_000 });
    }));
  }
  finally
  {
    await Promise.all(contexts.map(ctx => ctx.close()));
  }
});
