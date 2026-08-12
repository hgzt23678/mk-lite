import { expect, test, type Page, type Request, type Response } from '@playwright/test';

type PollState = {
  voteCalls: number;
  lastVoteChoice: number | null;
  poll: {
    multiple: boolean;
    votedByViewer: boolean;
    ownVotes: number[];
    options: Array<{ title: string; votesCount: number }>;
  };
};

function collectBrowserFailures(page: Page) {
  const failures: string[] = [];
  const recordRequestFailure = (request: Request) => {
    failures.push(`requestfailed ${request.method()} ${new URL(request.url()).pathname}: ${request.failure()?.errorText ?? 'unknown'}`);
  };
  const recordBadResponse = (response: Response) => {
    if (response.status() >= 400) {
      failures.push(`http ${response.status()} ${response.request().method()} ${new URL(response.url()).pathname}`);
    }
  };

  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console ${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`pageerror ${error.message}`));
  page.on('requestfailed', recordRequestFailure);
  page.on('response', recordBadResponse);
  return failures;
}

async function openAuthenticatedPoll(page: Page) {
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator('body > .dkgtipfy')).not.toHaveAttribute('inert', '');
  const poll = page.locator('.tkcbzcuz.qtqtichx .tivcixzd');
  await expect(poll).toBeVisible();
  await expect(poll.locator(':scope > ul > li')).toHaveCount(2);
  return poll;
}

async function confirmVote(page: Page, expectedVoteCalls: number) {
  const overlay = page.locator('.qzhlnise.dialog[role="alertdialog"]');
  await expect(overlay).toHaveCount(1);
  await expect.poll(() => overlay.getAttribute('data-motion-state')).toBe('entered');
  await expect(overlay.locator(':scope > .content > .mk-dialog > .icon.question')).toHaveCount(1);
  await expect(overlay.locator(':scope > .content > .mk-dialog > .buttons > button')).toHaveCount(2);

  const colors = await overlay.evaluate(element => {
    const dialog = element.querySelector(':scope > .content > .mk-dialog') as HTMLElement;
    const background = getComputedStyle(dialog).backgroundColor;
    const expected = getComputedStyle(document.documentElement).getPropertyValue('--panel').trim();
    const alpha = background.startsWith('rgba(')
      ? Number.parseFloat(background.split(',')[3] ?? '0')
      : 1;
    return { background, expected, alpha };
  });
  expect(colors.background).toBe(colors.expected);
  expect(colors.alpha).toBe(1);

  const ok = overlay.getByRole('button', { name: 'OK', exact: true });
  await expect(ok).toBeFocused();
  await ok.click();
  // A dialog only disappears after the server-side vote command has completed. Assert the
  // durable fixture transition first, so a rendering timeout cannot mask a rejected vote.
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/poll-state')).json() as PollState;
    return state;
  }).toMatchObject({ voteCalls: expectedVoteCalls });
  await expect(overlay).toHaveCount(0);
}

test.beforeEach(async ({ request }) => {
  const fixture = await request.post('/__test/poll-note/single');
  expect(fixture.status()).toBe(204);
  const diagnostics = await request.post('/__test/reset-diagnostics');
  expect(diagnostics.status()).toBe(204);
});

test.afterEach(async ({ request }) => {
  const reset = await request.post('/__test/poll-note/reset');
  expect(reset.status()).toBe(204);
});

test('single-choice poll preserves upstream DOM/CSS and persists one keyboard vote', async ({ page, request }) => {
  const browserFailures = collectBrowserFailures(page);
  const poll = await openAuthenticatedPoll(page);
  const choices = poll.locator(':scope > ul > li');
  const backdrops = choices.locator(':scope > .backdrop');

  await expect(poll).not.toHaveClass(/\bdone\b/);
  await expect(choices.nth(0)).toHaveAttribute('role', 'button');
  await expect(choices.nth(0)).toHaveAttribute('tabindex', '0');
  await expect(choices.nth(0)).toHaveAttribute('aria-disabled', 'false');
  await expect(choices.nth(0)).toHaveAttribute('aria-pressed', 'false');
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveCount(0);
  await expect(poll.locator(':scope > p')).toContainText('計3票');
  await expect(poll.locator(':scope > p > a[role="button"]')).toHaveText('結果を見る');

  const geometry = await choices.nth(0).evaluate(element => {
    const choice = getComputedStyle(element);
    const backdrop = getComputedStyle(element.querySelector(':scope > .backdrop')!);
    const label = getComputedStyle(element.querySelector(':scope > span')!);
    const root = getComputedStyle(document.documentElement);
    return {
      choiceDisplay: choice.display,
      choicePosition: choice.position,
      choiceMargin: choice.margin,
      choicePadding: choice.padding,
      choiceRadius: choice.borderRadius,
      choiceOverflow: choice.overflow,
      choiceCursor: choice.cursor,
      choiceBackground: choice.backgroundColor,
      expectedChoiceBackground: root.getPropertyValue('--accentedBg').trim(),
      backdropPosition: backdrop.position,
      backdropHeight: backdrop.height,
      backdropTransitionProperty: backdrop.transitionProperty,
      backdropTransitionDuration: backdrop.transitionDuration,
      backdropTransitionTiming: backdrop.transitionTimingFunction,
      backdropBackground: backdrop.backgroundImage,
      labelDisplay: label.display,
      labelPadding: label.padding,
      labelRadius: label.borderRadius,
      labelBackground: label.backgroundColor,
      expectedLabelBackground: root.getPropertyValue('--panel').trim(),
    };
  });
  expect(geometry).toMatchObject({
    choiceDisplay: 'block',
    choicePosition: 'relative',
    choiceMargin: '4px 0px',
    choicePadding: '4px',
    choiceRadius: '4px',
    choiceOverflow: 'hidden',
    choiceCursor: 'pointer',
    backdropPosition: 'absolute',
    backdropTransitionProperty: 'width',
    backdropTransitionDuration: '1s',
    backdropTransitionTiming: 'ease',
    labelDisplay: 'inline-block',
    labelPadding: '3px 5px',
    labelRadius: '3px',
  });
  expect(geometry.choiceBackground).toBe(geometry.expectedChoiceBackground);
  expect(geometry.labelBackground).toBe(geometry.expectedLabelBackground);
  expect(geometry.backdropBackground).toContain('linear-gradient');
  expect(Number.parseFloat(geometry.backdropHeight)).toBeGreaterThan(0);
  await expect(backdrops.nth(0)).toHaveAttribute('style', /width: 0%/);
  await expect(backdrops.nth(1)).toHaveAttribute('style', /width: 0%/);

  const resultToggle = poll.locator(':scope > p > a[role="button"]');
  await resultToggle.focus();
  await resultToggle.press('Enter');
  await expect(resultToggle).toHaveText('投票する');
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveText(['(2票)', '(1票)']);
  await expect(backdrops.nth(0)).toHaveAttribute('style', /width: 66\.667%/);
  await expect(backdrops.nth(1)).toHaveAttribute('style', /width: 33\.333%/);
  await resultToggle.press('Space');
  await expect(resultToggle).toHaveText('結果を見る');

  await choices.nth(1).focus();
  await choices.nth(1).press('Enter');
  const dialog = page.locator('.qzhlnise.dialog[role="alertdialog"]');
  await expect(dialog.locator('.mk-dialog > .body')).toContainText('「beta」に投票しますか？');
  await confirmVote(page, 1);

  await expect(poll).toHaveClass(/\bdone\b/);
  await expect(choices.nth(1)).toHaveClass(/\bvoted\b/);
  await expect(choices.nth(1)).toHaveAttribute('aria-pressed', 'true');
  await expect(choices.nth(1).locator(':scope > span > i.fas.fa-check')).toHaveCount(1);
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveText(['(2票)', '(2票)']);
  await expect(backdrops.nth(0)).toHaveAttribute('style', /width: 50%/);
  await expect(backdrops.nth(1)).toHaveAttribute('style', /width: 50%/);
  await expect(poll.locator(':scope > p')).toContainText('計4票');
  await expect(poll.locator(':scope > p')).toContainText('投票済み');
  await expect(choices.nth(0)).toHaveAttribute('tabindex', '-1');
  await expect(choices.nth(0)).toHaveCSS('cursor', 'default');

  const state = await (await request.get('/__test/poll-state')).json() as PollState;
  expect(state).toMatchObject({ voteCalls: 1, lastVoteChoice: 1 });
  expect(state.poll.ownVotes).toEqual([1]);
  expect(state.poll.options.map(option => option.votesCount)).toEqual([2, 2]);

  await choices.nth(0).click({ force: true });
  await expect(page.locator('.qzhlnise.dialog[role="alertdialog"]')).toHaveCount(0);
  const unchanged = await (await request.get('/__test/poll-state')).json() as PollState;
  expect(unchanged.voteCalls).toBe(1);
  expect(browserFailures).toEqual([]);
  const diagnostics = await (await request.get('/__test/diagnostics')).json();
  expect(diagnostics.unhandledExceptions).toEqual([]);
});

test('multiple-choice poll keeps voting enabled and records distinct choices', async ({ page, request }) => {
  const fixture = await request.post('/__test/poll-note/multiple');
  expect(fixture.status()).toBe(204);
  const browserFailures = collectBrowserFailures(page);
  const poll = await openAuthenticatedPoll(page);
  const choices = poll.locator(':scope > ul > li');

  await choices.nth(1).click();
  await confirmVote(page, 1);
  await expect(poll).not.toHaveClass(/\bdone\b/);
  await expect(choices.nth(1)).toHaveClass(/\bvoted\b/);
  await expect(choices.nth(0)).toHaveAttribute('tabindex', '0');
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveCount(0);

  await choices.nth(0).click();
  await confirmVote(page, 2);
  await expect(poll).not.toHaveClass(/\bdone\b/);
  await expect(choices.nth(0)).toHaveClass(/\bvoted\b/);
  await expect(choices.nth(1)).toHaveClass(/\bvoted\b/);

  const state = await (await request.get('/__test/poll-state')).json() as PollState;
  expect(state).toMatchObject({ voteCalls: 2, lastVoteChoice: 0 });
  expect(state.poll.ownVotes).toEqual([1, 0]);
  expect(state.poll.options.map(option => option.votesCount)).toEqual([3, 2]);

  await poll.locator(':scope > p > a[role="button"]').click();
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveText(['(3票)', '(2票)']);
  await expect(choices.nth(0).locator(':scope > .backdrop')).toHaveAttribute('style', /width: 60%/);
  await expect(choices.nth(1).locator(':scope > .backdrop')).toHaveAttribute('style', /width: 40%/);
  expect(browserFailures).toEqual([]);
});

test('expired poll exposes results but cannot open a vote dialog', async ({ page, request }) => {
  const fixture = await request.post('/__test/poll-note/expired');
  expect(fixture.status()).toBe(204);
  const browserFailures = collectBrowserFailures(page);
  const poll = await openAuthenticatedPoll(page);
  const choices = poll.locator(':scope > ul > li');

  await expect(poll).toHaveClass(/\bdone\b/);
  await expect(choices.nth(0)).toHaveAttribute('tabindex', '-1');
  await expect(choices.nth(0)).toHaveAttribute('aria-disabled', 'true');
  await expect(choices.nth(0)).toHaveCSS('cursor', 'default');
  await expect(poll.locator(':scope > ul > li > span > .votes')).toHaveText(['(2票)', '(1票)']);
  await expect(poll.locator(':scope > p')).toContainText('終了');
  await expect(poll.locator(':scope > p > a[role="button"]')).toHaveCount(0);

  await choices.nth(0).click({ force: true });
  await expect(page.locator('.qzhlnise.dialog[role="alertdialog"]')).toHaveCount(0);
  const state = await (await request.get('/__test/poll-state')).json() as PollState;
  expect(state.voteCalls).toBe(0);
  expect(browserFailures).toEqual([]);
});
