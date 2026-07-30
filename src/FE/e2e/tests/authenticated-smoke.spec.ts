import { expect, test } from '../support/test';
import { waitForOneTimeCode } from '../support/mailosaur';

test.describe('@live-authenticated Workslip smoke', () => {
  test('logs in with OTP, navigates the app, opens a form, and logs out', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-desktop', 'Authenticated OTP smoke runs once on desktop Chromium.');

    const email = process.env.E2E_EMAIL?.trim();
    const apiKey = process.env.E2E_MAILOSAUR_API_KEY?.trim();
    const serverId = process.env.E2E_MAILOSAUR_SERVER_ID?.trim();

    test.skip(
      !email || !apiKey || !serverId,
      'E2E_EMAIL, E2E_MAILOSAUR_API_KEY, and E2E_MAILOSAUR_SERVER_ID are required.',
    );

    const receivedAfter = new Date(Date.now() - 5_000);

    await page.goto('/login');
    await page.getByRole('button', { name: 'Mistet dit login? Modtag engangskode' }).click();
    await page.getByLabel('Email').fill(email!);
    await page.getByRole('button', { name: 'Send kode' }).click();

    await expect(page.getByText('En kode er sendt til')).toBeVisible();
    await expect(page.getByText(email!)).toBeVisible();

    const code = await waitForOneTimeCode({
      apiKey: apiKey!,
      serverId: serverId!,
      email: email!,
      receivedAfter,
    });

    await page.getByLabel('Engangskode').fill(code);
    await page.getByRole('button', { name: 'Log ind', exact: true }).click();

    await expect(page).toHaveURL(/\/app(?:\/|$)/);
    await expect(page.getByRole('link', { name: 'Sager' })).toBeVisible();

    await page.getByRole('link', { name: 'Timer' }).click();
    await expect(page).toHaveURL(/\/app\/timer$/);

    await page.getByRole('link', { name: 'Sager' }).click();
    await expect(page).toHaveURL(/\/app\/?$/);

    await page.getByRole('button', { name: 'Opret ny sag' }).click();
    await expect(page.getByRole('heading', { name: 'Opret' })).toBeVisible();
    await page.getByRole('button', { name: /^Diverse job/ }).click();

    await expect(page).toHaveURL(/\/app\/job\/simple\/new$/);
    await expect(page.getByRole('heading', { name: 'Simpelt job' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Opret job' })).toBeDisabled();

    await page.getByRole('button', { name: 'Tilbage' }).first().click();
    await expect(page).toHaveURL(/\/app\/?$/);

    await page.getByRole('button', { name: 'Log ud' }).click();
    await expect(page).toHaveURL(/\/login(?:\?|$)/);
    await expect(page.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeVisible();
  });
});
