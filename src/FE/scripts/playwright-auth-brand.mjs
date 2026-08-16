import assert from 'node:assert/strict';
import process from 'node:process';
import { requireLoopbackOrigin } from './playwright-ephemeral-auth.mjs';

const APP_URL = requireLoopbackOrigin(
  process.env.WORKSLIP_PLAYWRIGHT_APP_URL || 'http://127.0.0.1:5270',
  'WORKSLIP_PLAYWRIGHT_APP_URL',
);
const UI_TIMEOUT = 25_000;

const { chromium } = await import('playwright');
const browser = await chromium.launch({ headless: true });

const cases = [
  { name: 'day-desktop', theme: 'day', viewport: { width: 1280, height: 800 } },
  { name: 'night-mobile-390', theme: 'night', viewport: { width: 390, height: 844 } },
  { name: 'day-mobile-320', theme: 'day', viewport: { width: 320, height: 740 } },
];

try {
  for (const testCase of cases) {
    await verifyAuthBrandCase(testCase);
  }
  console.log('[playwright] auth brand day/night + responsive evidence passed.');
} finally {
  await browser.close();
}

async function verifyAuthBrandCase({ name, theme, viewport }) {
  const context = await browser.newContext({
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    viewport,
    colorScheme: theme === 'night' ? 'dark' : 'light',
  });

  await context.addInitScript(({ appOrigin, selectedTheme }) => {
    if (window.location.origin === appOrigin) {
      localStorage.setItem('theme', selectedTheme);
    }
  }, {
    appOrigin: new URL(APP_URL).origin,
    selectedTheme: theme,
  });

  try {
    const page = await context.newPage();
    const pageErrors = [];
    const consoleErrors = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });

    const navigation = await page.goto(`${APP_URL}/login`, {
      waitUntil: 'domcontentloaded',
      timeout: UI_TIMEOUT,
    });
    assert.ok(navigation?.ok(), `${name}: /login returned HTTP ${navigation?.status() ?? 'unknown'}.`);

    const authShell = page.locator('.auth-shell');
    await authShell.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const loginCard = page.locator('.login-card');
    await loginCard.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const styles = await page.evaluate(() => {
      const root = document.documentElement;
      const body = getComputedStyle(document.body);
      const cardElement = document.querySelector('.login-card');
      const buttonElement = document.querySelector('.login-submit-btn.btn-primary');
      const logoElement = document.querySelector('.auth-shell .logo-icon');
      if (!(cardElement instanceof HTMLElement)
        || !(buttonElement instanceof HTMLElement)
        || !(logoElement instanceof HTMLElement)) {
        throw new Error('Expected branded login elements were not rendered.');
      }

      const card = getComputedStyle(cardElement);
      const button = getComputedStyle(buttonElement);
      const logo = getComputedStyle(logoElement);
      const rect = cardElement.getBoundingClientRect();

      return {
        theme: root.getAttribute('data-theme'),
        bodyBackground: body.backgroundColor,
        primaryToken: body.getPropertyValue('--primary').trim(),
        selectionToken: body.getPropertyValue('--color-primary').trim(),
        focusToken: body.getPropertyValue('--focus-ring').trim(),
        cardBackground: card.backgroundColor,
        buttonBackground: button.backgroundColor,
        buttonColor: button.color,
        logoColor: logo.color,
        cardLeft: rect.left,
        cardRight: rect.right,
        viewportWidth: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        appShellCount: document.querySelectorAll('.app-shell').length,
      };
    });

    assert.equal(styles.theme, theme, `${name}: stored theme must apply before the login surface is evaluated.`);
    assert.equal(styles.primaryToken, '#f47a24', `${name}: primary action token must resolve to Workslip signal orange.`);
    assert.equal(styles.selectionToken, '#147a7e', `${name}: selection/information token must resolve to Workslip petrol.`);
    assert.equal(
      styles.focusToken,
      theme === 'day' ? '#147a7e' : '#55b8b8',
      `${name}: focus token must remain informational/petrol rather than action orange.`,
    );
    assert.equal(
      styles.bodyBackground,
      theme === 'day' ? 'rgb(255, 247, 232)' : 'rgb(13, 48, 59)',
      `${name}: login canvas must use the Workslip day/night brand canvas.`,
    );
    assert.equal(
      styles.cardBackground,
      theme === 'day' ? 'rgb(255, 255, 255)' : 'rgb(18, 59, 74)',
      `${name}: login card must use the shared Workslip floating surface.`,
    );
    assert.equal(styles.buttonBackground, 'rgb(244, 122, 36)', `${name}: primary login action must be signal orange.`);
    assert.equal(styles.buttonColor, 'rgb(18, 59, 74)', `${name}: orange primary action must use marine foreground for contrast.`);
    assert.equal(styles.logoColor, 'rgb(244, 122, 36)', `${name}: Workslip login mark must use the brand action accent.`);
    assert.equal(styles.appShellCount, 0, `${name}: public login must not mount the authenticated app shell.`);
    assert.ok(styles.cardLeft >= 0, `${name}: login card must not overflow the left viewport edge.`);
    assert.ok(styles.cardRight <= styles.viewportWidth + 0.5, `${name}: login card must not overflow the right viewport edge.`);
    assert.ok(styles.documentWidth <= styles.viewportWidth, `${name}: login document must not create horizontal scrolling.`);

    await page.getByRole('button', { name: /engangskode/i }).click();
    await page.locator('.login-card').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const otcBounds = await page.locator('.login-card').boundingBox();
    assert.ok(otcBounds, `${name}: OTC module must remain inside the login card.`);
    assert.ok(otcBounds.x >= 0, `${name}: OTC card must not overflow the left viewport edge.`);
    assert.ok(otcBounds.x + otcBounds.width <= viewport.width + 0.5, `${name}: OTC card must not overflow the right viewport edge.`);

    assert.deepEqual(pageErrors, [], `${name}: browser page errors: ${pageErrors.join(' | ')}`);
    assert.deepEqual(consoleErrors, [], `${name}: browser console errors: ${consoleErrors.join(' | ')}`);
  } finally {
    await context.close();
  }
}
