import { expect, openSceneObject, runInConsole, test } from './fixtures';

test('scene tree opens GameObjects, components and editable members', async ({ app }) => {
  await openSceneObject(app, 'Player');
  await expect(app.locator('.obj-node .pill', { hasText: 'Main' })).toBeVisible();

  const card = app.locator('.comp-card', { hasText: 'PlayerController' });
  await card.locator('.comp-head').click();
  const speed = card.locator('.v-row', { hasText: 'speed' });
  await expect(speed.locator('.v-val')).toContainText(/\d/);

  await speed.locator('.v-val').dblclick();
  await speed.locator('.v-edit input').fill('9.5');
  await app.keyboard.press('Enter');
  await expect(speed.locator('.v-val .val')).toHaveText('9.5');
  await expect(app.locator('.toast.ok')).toContainText('speed');
});

test('expands nested values and collections', async ({ app }) => {
  await openSceneObject(app, 'Player');
  const card = app.locator('.comp-card', { hasText: 'PlayerController' });
  await card.locator('.comp-head').click();
  await card.locator('.v-row', { hasText: 'inventory' }).locator('.v-name').click();
  await expect(card.locator('.v-row', { hasText: '[3]' })).toContainText('Map');
});

test('console runs code, completes members and recalls history', async ({ app }) => {
  await runInConsole(app, 'var answer = 6 * 7;');
  await runInConsole(app, 'answer');
  await expect(app.locator('.c-entry.res').last()).toContainText('42', { timeout: 15_000 });

  const editor = app.locator('.console-editor .cm-content');
  await editor.click();
  await app.keyboard.type('Time.fram');
  await expect(app.locator('.cm-tooltip-autocomplete li[aria-selected]')).toContainText('frameCount');
  await app.waitForTimeout(150); // CodeMirror ignores accepting within 75 ms of the list changing
  await app.keyboard.press('Tab');
  await expect(editor).toHaveText('Time.frameCount');
  await app.keyboard.press('Enter');
  await expect(app.locator('.c-entry.res').last()).toContainText(/\d+/);

  await app.keyboard.press('ArrowUp');
  await expect(editor).toHaveText('Time.frameCount');
});

test('compile errors are listed with their position', async ({ app }) => {
  await runInConsole(app, 'int x = ;');
  await expect(app.locator('.c-entry.err .diag')).toContainText('CS1525');
});

test('types search opens static members and instances', async ({ app }) => {
  await app.locator('.side-tabs .tab', { hasText: '类型' }).click();
  await app.locator('.side-panel:not([hidden]) input[type=search]').fill('GameManager');
  await app.locator('.side-panel:not([hidden]) .t-row .label', { hasText: /^GameManager$/ }).click();
  await expect(app.locator('.obj-kind.static')).toBeVisible();
  await app.locator('.obj-tabs .tab', { hasText: '实例' }).click();
  await expect(app.locator('.obj-pane .val.link')).toHaveCount(1);
});

test('hooks a method from its method list and streams calls', async ({ app }) => {
  await openSceneObject(app, 'Player');
  await app.locator('.comp-card', { hasText: 'PlayerController' }).locator('.icon-btn[title="单独打开"]').click();
  await app.locator('.obj-tabs .tab', { hasText: '方法' }).click();
  const row = app.locator('.m-head', { hasText: 'TakeDamage' });
  await row.hover();
  await row.locator('.icon-btn').click();

  await expect(app.locator('.candidate')).toContainText('TakeDamage(int)');
  await app.locator('.modal .btn.primary', { hasText: '挂钩' }).click();
  await expect(app.locator('.hook-item.selected')).toContainText('TakeDamage(int)');
  await expect(app.locator('.call').first()).toContainText('amount', { timeout: 10_000 });

  await app.locator('.hook-item.selected .icon-btn[title="移除 Hook"]').click();
  await expect(app.locator('.hook-item')).toHaveCount(0);
});

test('warns about folded (ICF) methods before hooking them', async ({ app }) => {
  await app.keyboard.press('Control+k');
  await app.locator('.palette-input input').fill('> 新建 Hook');
  await app.keyboard.press('Enter');
  await app.locator('.hook-form input.input').first().fill('PlayerController::OnRespawn');
  await app.locator('.hook-form .btn', { hasText: '解析' }).click();

  await expect(app.locator('.icf-warning')).toContainText('Enemy::OnRespawn');
  const create = app.locator('.modal .btn.primary', { hasText: '挂钩' });
  await expect(create).toBeDisabled();
  await app.locator('.icf-warning input[type=checkbox]').check();
  await create.click();
  await expect(app.locator('.hook-item.selected')).toContainText('ICF');

  await app.locator('.hook-item.selected .icon-btn[title="移除 Hook"]').click();
});

test('logs stream and filter', async ({ app }) => {
  await runInConsole(app, 'Debug.LogWarning("e2e marker");');
  await app.locator('.drawer-tabs .tab', { hasText: '日志' }).click();
  await expect(app.locator('.lg.l-warn', { hasText: 'e2e marker' })).toBeVisible();
  await app.locator('.log-toolbar input[type=search]').fill('e2e marker');
  await expect(app.locator('.lg')).toHaveCount(1);
});

test('command palette finds GameObjects', async ({ app }) => {
  await app.keyboard.press('Control+k');
  await app.locator('.palette-input input').fill('Enemy (2)');
  await expect(app.locator('.p-item', { hasText: 'Enemy (2)' })).toBeVisible();
  await app.keyboard.press('Enter');
  await expect(app.locator('.obj-title')).toHaveText('Enemy (2)');
});
