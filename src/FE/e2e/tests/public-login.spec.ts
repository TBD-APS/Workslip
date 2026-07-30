import { expect, test } from '../support/test';

test.describe('@public one-time-code login', () => {
  test('validates the form, enters the code step, and returns to passkey login', async ({ page }) => {
    await page.route('**/api/auth/send-code', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: '{}',
      });
    });

    await page.goto('/login');

    await expect(page.getByRole('heading', { name: 'Log ind på Workslip' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeVisible();

    await page.getByRole('button', { name: 'Mistet dit login? Modtag engangskode' }).click();
    const emailInput = page.getByLabel('Email');
    await expect(emailInput).toBeVisible();

    await emailInput.fill('ikke-en-email');
    await page.getByRole('button', { name: 'Send kode' }).click();
    await expect(page.getByText('Ugyldig email adresse')).toBeVisible();

    const testEmail = 'playwright@example.test';
    await emailInput.fill(testEmail);
    await page.getByRole('button', { name: 'Send kode' }).click();

    await expect(page.getByText('En kode er sendt til')).toBeVisible();
    await expect(page.getByText(testEmail)).toBeVisible();

    await page.getByLabel('Engangskode').fill('123');
    await page.getByRole('button', { name: 'Log ind', exact: true }).click();
    await expect(page.getByText('Koden skal bestå af 6 cifre')).toBeVisible();

    await page.getByRole('button', { name: 'Tilbage til login' }).click();
    await expect(page.getByRole('button', { name: 'Log ind med Microsoft passkey' })).toBeVisible();
  });
});
