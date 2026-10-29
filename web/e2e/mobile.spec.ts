import { expect, test } from './fixtures';

test('phones show one pane at a time without horizontal scrolling', async ({ app }) => {
  const overflow = () => app.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(await overflow()).toBeLessThanOrEqual(0);

  await expect(app.locator('.mobile-nav')).toBeVisible();
  await app.locator('.side-panel:not([hidden]) .t-row .label', { hasText: /^Player$/ }).tap();
  await expect(app.locator('.inspector')).toBeVisible();
  await expect(app.locator('.sidebar')).toBeHidden();
  expect(await overflow()).toBeLessThanOrEqual(0);

  await app.locator('.mobile-nav button', { hasText: '控制台' }).tap();
  await expect(app.locator('.console-editor')).toBeVisible();
  await app.locator('.drawer-tabs .tab', { hasText: 'Hook' }).tap();
  await expect(app.locator('.hooks')).toBeVisible();
  expect(await overflow()).toBeLessThanOrEqual(0);
});
