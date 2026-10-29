import { expect, test as base, type Page } from '@playwright/test';

/** A signed-in page that fails the test on any console error. */
export const test = base.extend<{ app: Page }>({
  app: async ({ page }, use) => {
    const errors: string[] = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => m.type() === 'error' && !m.text().includes('status of 4') && errors.push(m.text()));
    await page.goto('/#token=e2e');
    await expect(page.locator('.t-row.scene').first()).toBeVisible();
    await use(page);
    expect(errors).toEqual([]);
  },
});

export { expect };

export async function openSceneObject(page: Page, name: string) {
  await page.locator('.side-panel:not([hidden]) .t-row .label', { hasText: new RegExp(`^${name}$`) }).first().click();
  await expect(page.locator('.obj-title')).toHaveText(name);
}

export async function runInConsole(page: Page, code: string) {
  const editor = page.locator('.console-editor .cm-content');
  await editor.click();
  await page.keyboard.type(code);
  await page.keyboard.press('Escape');
  await page.keyboard.press('Control+Enter');
}
