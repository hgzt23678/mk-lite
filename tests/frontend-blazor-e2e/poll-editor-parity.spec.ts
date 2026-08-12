import { expect, test } from '@playwright/test';

test('MkPollEditor preserves the Misskey v12 editor DOM, CSS and mutation behavior', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/poll-editor');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');

  const editor = page.locator('.zmdxowus.contract-poll-editor');
  const state = page.locator('[data-contract="state"]');
  await expect(editor).toHaveAttribute('data-fallthrough', 'poll-editor');
  await expect(editor.locator(':scope > ul > li')).toHaveCount(2);
  await expect(editor.locator(':scope > ul > li input').nth(0)).toHaveAttribute('placeholder', '選択肢1');
  await expect(editor.locator(':scope > ul > li input').nth(1)).toHaveAttribute('placeholder', '選択肢2');
  await expect(editor.locator(':scope > .caution')).toHaveCount(0);
  await expect(editor.locator(':scope > .ziffeomt > .label > span')).toHaveText('複数回答可');
  await expect(editor.locator(':scope > section > div > .vblkjoeq > .label')).toHaveText('期限');

  const geometry = await editor.evaluate(element => {
    const root = getComputedStyle(element);
    const choice = getComputedStyle(element.querySelector(':scope > ul > li')!);
    const input = getComputedStyle(element.querySelector(':scope > ul > li > .input')!);
    const remove = getComputedStyle(element.querySelector(':scope > ul > li > button')!);
    const fields = getComputedStyle(element.querySelector(':scope > section > div')!);
    return {
      padding: root.padding,
      choiceDisplay: choice.display,
      choiceMargin: choice.margin,
      choiceWidth: choice.width,
      inputFlexGrow: input.flexGrow,
      removeWidth: remove.width,
      removePadding: remove.padding,
      fieldsDisplay: fields.display,
      fieldsGap: fields.gap,
      fieldsWrap: fields.flexWrap,
    };
  });
  expect(geometry).toMatchObject({
    padding: '8px 16px',
    choiceDisplay: 'flex',
    choiceMargin: '8px 0px',
    inputFlexGrow: '1',
    removeWidth: '32px',
    removePadding: '4px 0px',
    fieldsDisplay: 'flex',
    fieldsGap: '12px',
    fieldsWrap: 'wrap',
  });

  await editor.locator(':scope > ul > li:first-child > button._button').click();
  await expect(editor.locator(':scope > ul > li')).toHaveCount(1);
  await expect(editor.locator(':scope > .caution')).toHaveText('選択肢は最低2つ必要です');
  await expect(state).toHaveAttribute('data-choices', '1');

  await editor.locator(':scope > button.add').click();
  await expect(editor.locator(':scope > ul > li')).toHaveCount(2);
  await editor.locator(':scope > ul > li').nth(0).locator('input').fill('alpha');
  await editor.locator(':scope > ul > li').nth(1).locator('input').fill('beta');

  await editor.locator(':scope > .ziffeomt > .button').click();
  await expect(editor.locator(':scope > .ziffeomt')).toHaveClass(/\bchecked\b/);
  await expect(state).toHaveAttribute('data-multiple', 'true');

  const expiration = editor.locator(':scope > section > div > .vblkjoeq select');
  await expiration.selectOption('at');
  await editor.locator('input[type="date"]').fill('2026-08-10');
  await editor.locator('input[type="time"]').fill('12:30');
  await expect(state).toHaveAttribute('data-expiration', 'at');
  await expect(state).toHaveAttribute('data-date', '2026-08-10');
  await expect(state).toHaveAttribute('data-time', '12:30');

  await expiration.selectOption('after');
  await editor.locator('input[type="number"]').fill('2');
  await editor.locator(':scope > section > div > section .vblkjoeq select').selectOption('day');
  await expect(state).toHaveAttribute('data-expiration', 'after');
  await expect(state).toHaveAttribute('data-after', '2');
  await expect(state).toHaveAttribute('data-unit', 'day');

  for (let index = 0; index < 8; index++) {
    await editor.locator(':scope > button.add').click();
  }
  await expect(editor.locator(':scope > ul > li')).toHaveCount(10);
  await expect(editor.locator(':scope > button.add')).toBeDisabled();
  await expect(editor.locator(':scope > button.add')).toHaveText('これ以上追加できません');
  await expect(state).toHaveAttribute('data-choices', '10');

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
