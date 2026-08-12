import { expect, test } from '@playwright/test';

test('MkLoading preserves the pinned CSS-module geometry and spinner lifecycle', async ({ page }) => {
  await page.goto('/');

  await page.evaluate(() => {
    const root = document.createElement('div');
    root.id = 'mk-loading-browser-contract';
    root.className = '_root_13vug_9 _colored_13vug_15';
    root.setAttribute('role', 'status');
    root.setAttribute('aria-label', '読み込み中');
    root.setAttribute('aria-busy', 'true');
    root.innerHTML = `
      <div class="_container_13vug_28">
        <svg class="_spinner_13vug_35 _bg_13vug_48" viewBox="0 0 168 168" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <g transform="matrix(1.125,0,0,1.125,12,12)">
            <circle cx="64" cy="64" r="64" style="fill:none;stroke:currentColor;stroke-width:21.33px"></circle>
          </g>
        </svg>
        <svg class="_spinner_13vug_35 _fg_13vug_52" viewBox="0 0 168 168" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <g transform="matrix(1.125,0,0,1.125,12,12)">
            <path d="M128,64C128,28.654 99.346,0 64,0C99.346,0 128,28.654 128,64Z" style="fill:none;stroke:currentColor;stroke-width:21.33px"></path>
          </g>
        </svg>
      </div>`;
    document.body.append(root);
  });

  const root = page.locator('#mk-loading-browser-contract');
  await expect(root).toHaveAttribute('role', 'status');
  await expect(root).toHaveAttribute('aria-label', '読み込み中');
  await expect(root.locator(':scope > ._container_13vug_28 > svg')).toHaveCount(2);

  const normal = await root.evaluate(element => {
    const rootStyle = getComputedStyle(element);
    const container = element.querySelector('._container_13vug_28')!;
    const background = element.querySelector('._bg_13vug_48')!;
    const foreground = element.querySelector('._fg_13vug_52')!;
    const containerStyle = getComputedStyle(container);
    const foregroundStyle = getComputedStyle(foreground);
    return {
      padding: rootStyle.padding,
      cursor: rootStyle.cursor,
      size: rootStyle.getPropertyValue('--size').trim(),
      containerWidth: containerStyle.width,
      containerHeight: containerStyle.height,
      backgroundOpacity: getComputedStyle(background).opacity,
      animationName: foregroundStyle.animationName,
      animationDuration: foregroundStyle.animationDuration,
      animationTimingFunction: foregroundStyle.animationTimingFunction,
      animationIterationCount: foregroundStyle.animationIterationCount,
      firstTransform: foregroundStyle.transform,
    };
  });

  expect(normal).toEqual({
    padding: '32px',
    cursor: 'wait',
    size: '38px',
    containerWidth: '38px',
    containerHeight: '38px',
    backgroundOpacity: '0.275',
    animationName: '_spinner_13vug_35',
    animationDuration: '0.5s',
    animationTimingFunction: 'linear',
    animationIterationCount: 'infinite',
    firstTransform: normal.firstTransform,
  });

  await page.waitForTimeout(140);
  const laterTransform = await root.locator('._fg_13vug_52').evaluate(
    element => getComputedStyle(element).transform);
  expect(laterTransform).not.toBe(normal.firstTransform);

  const variants = await root.evaluate(element => {
    element.className = '_root_13vug_9 _inline_13vug_18 _colored_13vug_15';
    const inline = getComputedStyle(element);
    const inlineValues = {
      display: inline.display,
      padding: inline.padding,
      size: inline.getPropertyValue('--size').trim(),
    };
    element.className = '_root_13vug_9 _mini_13vug_23';
    const mini = getComputedStyle(element);
    return {
      inline: inlineValues,
      mini: {
        padding: mini.padding,
        size: mini.getPropertyValue('--size').trim(),
      },
    };
  });

  expect(variants).toEqual({
    inline: { display: 'inline', padding: '0px', size: '32px' },
    mini: { padding: '16px', size: '32px' },
  });
});

test('MkLoading obeys the application reduced-motion contract', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  const motion = await page.evaluate(() => {
    const foreground = document.createElement('svg');
    foreground.className.baseVal = '_spinner_13vug_35 _fg_13vug_52';
    document.body.append(foreground);
    const style = getComputedStyle(foreground);
    return {
      durationSeconds: Number.parseFloat(style.animationDuration) *
        (style.animationDuration.endsWith('ms') ? 0.001 : 1),
      iterationCount: style.animationIterationCount,
    };
  });

  expect(motion.durationSeconds).toBeLessThanOrEqual(0.000001);
  expect(motion.iterationCount).toBe('1');
});

test('MkEllipsis preserves the pinned stagger and scoped keyframe name', async ({ page }) => {
  await page.goto('/');

  const contract = await page.evaluate(async () => {
    const root = document.createElement('span');
    root.id = 'mk-ellipsis-browser-contract';
    root.className = 'mk-ellipsis';
    root.setAttribute('aria-hidden', 'true');
    root.innerHTML = '<span>.</span><span>.</span><span>.</span>';
    document.body.append(root);

    const dots = Array.from(root.children) as HTMLElement[];
    const styles = dots.map(dot => {
      const style = getComputedStyle(dot);
      return {
        name: style.animationName,
        duration: style.animationDuration,
        delay: style.animationDelay,
        timing: style.animationTimingFunction,
        iterations: style.animationIterationCount,
        fill: style.animationFillMode,
      };
    });

    const animation = dots[0].getAnimations()[0];
    animation.pause();
    animation.currentTime = 560;
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    return {
      hidden: root.getAttribute('aria-hidden'),
      text: dots.map(dot => dot.textContent),
      styles,
      opacityAtFortyPercent: getComputedStyle(dots[0]).opacity,
    };
  });

  expect(contract.hidden).toBe('true');
  expect(contract.text).toEqual(['.', '.', '.']);
  expect(contract.styles).toEqual([
    {
      name: 'ellipsis-abe8165c', duration: '1.4s', delay: '0s',
      timing: 'ease-in-out', iterations: 'infinite', fill: 'both'
    },
    {
      name: 'ellipsis-abe8165c', duration: '1.4s', delay: '0.16s',
      timing: 'ease-in-out', iterations: 'infinite', fill: 'both'
    },
    {
      name: 'ellipsis-abe8165c', duration: '1.4s', delay: '0.32s',
      timing: 'ease-in-out', iterations: 'infinite', fill: 'both'
    },
  ]);
  expect(Number.parseFloat(contract.opacityAtFortyPercent)).toBeLessThan(0.01);
});

test('MkInfo preserves the pinned normal and warning surfaces', async ({ page }) => {
  await page.goto('/');

  const surfaces = await page.evaluate(() => {
    const root = document.createElement('div');
    root.id = 'mk-info-browser-contract';
    root.className = 'fpezltsf';
    root.innerHTML = '<i class="fas fa-info-circle" aria-hidden="true"></i>診断メッセージ';
    document.body.append(root);

    const read = () => {
      const style = getComputedStyle(root);
      return {
        padding: style.padding,
        fontSize: style.fontSize,
        background: style.backgroundColor,
        color: style.color,
        borderRadius: style.borderRadius,
        iconMargin: getComputedStyle(root.querySelector('i')!).marginRight,
      };
    };

    const normal = read();
    root.classList.add('warn');
    return { normal, warning: read() };
  });

  expect(surfaces.normal.padding).toBe('16px');
  expect(surfaces.normal.borderRadius).toBe('12px');
  expect(surfaces.normal.iconMargin).toBe('4px');
  expect(surfaces.normal.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(surfaces.warning.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(surfaces.warning.background).not.toBe(surfaces.normal.background);
  expect(surfaces.warning.color).not.toBe(surfaces.normal.color);
});

test('FormSection preserves the pinned borders slot geometry and sibling rules', async ({ page }) => {
  await page.goto('/about-misskey');

  const sections = page.locator('.znqjceqz > .vrtktovh._formBlock');
  await expect(sections).toHaveCount(3);
  const geometry = await sections.evaluateAll(elements => elements.map(element => {
    const root = getComputedStyle(element);
    const label = getComputedStyle(element.querySelector(':scope > .label')!);
    const main = getComputedStyle(element.querySelector(':scope > .main._formRoot')!);
    return {
      top: root.borderTopWidth,
      bottom: root.borderBottomWidth,
      labelWeight: label.fontWeight,
      labelMargin: label.margin,
      mainMargin: main.margin,
    };
  }));

  expect(Number.parseFloat(geometry[0].top)).toBeGreaterThan(0);
  expect(geometry[1].top).toBe('0px');
  expect(geometry[2].top).toBe('0px');
  expect(geometry[2].bottom).toBe('0px');
  for (const section of geometry) {
    expect(Number.parseInt(section.labelWeight, 10)).toBeGreaterThanOrEqual(700);
    expect(section.labelMargin).toBe('21px 0px 16px');
    expect(section.mainMargin).toBe('21px 0px');
  }
});
