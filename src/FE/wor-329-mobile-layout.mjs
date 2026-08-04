import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const [baseCss, appCss] = await Promise.all([
  readFile(new URL('./src/base.css', import.meta.url), 'utf8'),
  readFile(new URL('./src/App.css', import.meta.url), 'utf8'),
]);

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 390, height: 844 } });

try {
  await page.setContent(`
    <style>${baseCss}\n${appCss}</style>
    <div class="app-shell">
      <header class="app-header"><strong>Workslip</strong></header>
      <main class="app-content">
        <div class="page-container time-overview-page">
          <div style="min-height: 1250px">Timer</div>
          <div id="last-hours-row" style="height: 40px">Sidste timerække</div>
        </div>
      </main>
      <nav class="bottom-nav" aria-label="Primær navigation">
        ${['Sager', 'Timer', 'Folk', 'Kunder'].map((label) => `
          <a class="nav-item" href="#">
            <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden="true"></svg>
            <span>${label}</span>
          </a>
        `).join('')}
      </nav>
    </div>
  `);

  await page.locator('.app-shell').evaluate((element) => {
    element.scrollTo({ top: element.scrollHeight });
  });
  await page.waitForTimeout(50);

  const metrics = await page.evaluate(() => {
    const content = document.querySelector('.app-content');
    const navigation = document.querySelector('.bottom-nav');
    const lastRow = document.querySelector('#last-hours-row');

    if (!(content instanceof HTMLElement)
      || !(navigation instanceof HTMLElement)
      || !(lastRow instanceof HTMLElement)) {
      throw new Error('Validation fixture is incomplete.');
    }

    const navigationRect = navigation.getBoundingClientRect();
    const lastRowRect = lastRow.getBoundingClientRect();

    return {
      contentPaddingBottom: Number.parseFloat(getComputedStyle(content).paddingBottom),
      navigationHeight: navigationRect.height,
      navigationTop: navigationRect.top,
      lastRowBottom: lastRowRect.bottom,
      clearance: navigationRect.top - lastRowRect.bottom,
    };
  });

  if (metrics.contentPaddingBottom < metrics.navigationHeight + 8) {
    throw new Error(
      `Reserved content space ${metrics.contentPaddingBottom}px does not clear the ${metrics.navigationHeight}px navigation.`,
    );
  }

  if (metrics.clearance < 8) {
    throw new Error(`The final Hours row has only ${metrics.clearance}px clearance above navigation.`);
  }

  console.log('WOR-329 mobile layout verified:', metrics);
} finally {
  await browser.close();
}
