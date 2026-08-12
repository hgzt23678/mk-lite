const graduationsPadding = 0.5;
const textsPadding = 0.6;
const handsPadding = 1;
const handsTailLength = 0.7;
const hourHandLengthRatio = 0.75;
const minuteHandLengthRatio = 1;
const secondHandLengthRatio = 1;
const numbersOpacityFactor = 0.35;

function angleDiff(a, b) {
  const difference = Math.abs(a - b);
  return Math.abs((difference + Math.PI) % (Math.PI * 2) - Math.PI);
}

function parseComputedColor(value) {
  if (typeof value !== 'string' || value.trim().length === 0 || value.length > 128) {
    throw new Error('MISSKEY_ANALOG_CLOCK_THEME_COLOR_INVALID');
  }

  const probe = document.createElement('span');
  probe.style.position = 'fixed';
  probe.style.visibility = 'hidden';
  probe.style.pointerEvents = 'none';
  probe.style.color = value;
  if (probe.style.color.length === 0) {
    throw new Error('MISSKEY_ANALOG_CLOCK_THEME_COLOR_INVALID');
  }

  document.documentElement.append(probe);
  const normalized = getComputedStyle(probe).color;
  probe.remove();
  const match = normalized.match(/^rgba?\(\s*(\d+(?:\.\d+)?)\s*[, ]\s*(\d+(?:\.\d+)?)\s*[, ]\s*(\d+(?:\.\d+)?)/i);
  if (match === null) {
    throw new Error('MISSKEY_ANALOG_CLOCK_THEME_COLOR_INVALID');
  }

  return match.slice(1, 4).map(channel => Math.max(0, Math.min(255, Math.round(Number(channel)))));
}

function toHex(channels) {
  return `#${channels.map(channel => channel.toString(16).padStart(2, '0')).join('')}`;
}

function isDark(channels) {
  return ((channels[0] * 299) + (channels[1] * 587) + (channels[2] * 114)) / 1000 < 128;
}

function setNumberAttribute(element, name, value) {
  element.setAttribute(name, value.toString());
}

export function attach(
  element,
  thickness,
  offsetMinutes,
  twentyFourHour,
  graduations,
  fadeGraduations,
  secondHandAnimation) {
  if (!(element instanceof SVGSVGElement) || !Number.isFinite(thickness) || thickness < 0 ||
      offsetMinutes !== null && !Number.isFinite(offsetMinutes) ||
      typeof twentyFourHour !== 'boolean' || !['none', 'dots', 'numbers'].includes(graduations) ||
      typeof fadeGraduations !== 'boolean' || !['none', 'elastic', 'easeOut'].includes(secondHandAnimation)) {
    throw new Error('MISSKEY_ANALOG_CLOCK_CONFIGURATION_INVALID');
  }

  const expectedGraduations = graduations === 'none' ? 0 : (twentyFourHour ? 24 : 12);
  const graduationNodes = graduations === 'dots'
    ? Array.from(element.querySelectorAll(':scope > circle'))
    : graduations === 'numbers'
      ? Array.from(element.querySelectorAll(':scope > text'))
      : [];
  const lines = Array.from(element.querySelectorAll(':scope > line'));
  if (graduationNodes.length !== expectedGraduations || lines.length !== 3 ||
      graduations === 'dots' && graduationNodes.some(node => !(node instanceof SVGCircleElement)) ||
      graduations === 'numbers' && graduationNodes.some(node => !(node instanceof SVGTextElement))) {
    throw new Error('MISSKEY_ANALOG_CLOCK_DOM_INVALID');
  }

  const secondHand = lines[0];
  const minuteHand = lines[1];
  const hourHand = lines[2];
  if (!secondHand.classList.contains('s')) {
    throw new Error('MISSKEY_ANALOG_CLOCK_DOM_INVALID');
  }

  const angles = Array.from({ length: expectedGraduations }, (_, index) =>
    Math.PI * index / ((twentyFourHour ? 24 : 12) / 2));
  const effectiveOffset = offsetMinutes ?? -new Date().getTimezoneOffset();
  const timeouts = new Set();
  let disposed = false;
  let updateTimeout = 0;
  let previousSecondWasFiftyNine = false;
  let majorGraduationColor;
  let nowColor;

  const schedule = (callback, delay) => {
    const timeout = window.setTimeout(() => {
      timeouts.delete(timeout);
      if (!disposed) callback();
    }, delay);
    timeouts.add(timeout);
    return timeout;
  };

  const applySecondHandClass = disabled => {
    secondHand.className.baseVal = 's';
    if (!disabled && secondHandAnimation !== 'none') {
      secondHand.classList.add('animate', secondHandAnimation);
    }
  };

  const calculateColors = () => {
    if (disposed) return;
    const style = getComputedStyle(document.documentElement);
    const background = parseComputedColor(style.getPropertyValue('--bg'));
    const accent = toHex(parseComputedColor(style.getPropertyValue('--accent')));
    const foreground = toHex(parseComputedColor(style.getPropertyValue('--fg')));
    const dark = isDark(background);
    majorGraduationColor = dark ? 'rgba(255, 255, 255, 0.3)' : 'rgba(0, 0, 0, 0.3)';
    const secondHandColor = dark ? 'rgba(255, 255, 255, 0.5)' : 'rgba(0, 0, 0, 0.3)';
    nowColor = accent;
    secondHand.setAttribute('stroke', secondHandColor);
    minuteHand.setAttribute('stroke', foreground);
    hourHand.setAttribute('stroke', accent);
  };

  const renderGraduations = (hour, hourAngle) => {
    const current = twentyFourHour ? hour : hour % 12;
    for (let index = 0; index < graduationNodes.length; index++) {
      const node = graduationNodes[index];
      const active = current === index;
      const opacity = !fadeGraduations || active
        ? 1
        : Math.max(0, 1 - (angleDiff(hourAngle, angles[index]) / Math.PI) - numbersOpacityFactor);
      if (node instanceof SVGCircleElement) {
        setNumberAttribute(node, 'cx', 5 + (Math.sin(angles[index]) * (5 - graduationsPadding)));
        setNumberAttribute(node, 'cy', 5 - (Math.cos(angles[index]) * (5 - graduationsPadding)));
        node.setAttribute('r', '0.125');
        node.setAttribute('fill', active ? nowColor : majorGraduationColor);
      } else {
        setNumberAttribute(node, 'x', 5 + (Math.sin(angles[index]) * (5 - textsPadding)));
        setNumberAttribute(node, 'y', 5 - (Math.cos(angles[index]) * (5 - textsPadding)));
        node.setAttribute('font-size', active ? '1' : '0.7');
        node.setAttribute('font-weight', active ? 'bold' : 'normal');
        node.setAttribute('fill', active ? nowColor : 'currentColor');
      }
      setNumberAttribute(node, 'opacity', opacity);
    }
  };

  const tick = () => {
    if (disposed || !element.isConnected) return;
    const now = new Date();
    now.setMinutes(now.getMinutes() + now.getTimezoneOffset() + effectiveOffset);
    const second = now.getSeconds();
    const minute = now.getMinutes();
    const hour = now.getHours();
    const hourAngle = Math.PI * (hour % (twentyFourHour ? 24 : 12) + ((minute + (second / 60)) / 60)) /
      (twentyFourHour ? 12 : 6);
    const minuteAngle = Math.PI * (minute + (second / 60)) / 30;
    let secondAngle;
    // Timers may run more than once during :59. The wrap correction belongs to the
    // actual transition into :00, never to a duplicate render of the preceding second.
    if (previousSecondWasFiftyNine && second === 0) {
      secondAngle = Math.PI * 2;
      schedule(() => {
        applySecondHandClass(true);
        schedule(() => {
          secondHand.style.transform = 'rotateZ(0rad)';
          schedule(() => applySecondHandClass(false), 100);
        }, 100);
      }, 700);
    } else {
      secondAngle = Math.PI * second / 30;
    }
    previousSecondWasFiftyNine = second === 59;

    renderGraduations(hour, hourAngle);
    secondHand.style.transform = `rotateZ(${secondAngle}rad)`;
    setNumberAttribute(minuteHand, 'x1', 5 - (Math.sin(minuteAngle) * (minuteHandLengthRatio * handsTailLength)));
    setNumberAttribute(minuteHand, 'y1', 5 + (Math.cos(minuteAngle) * (minuteHandLengthRatio * handsTailLength)));
    setNumberAttribute(minuteHand, 'x2', 5 + (Math.sin(minuteAngle) * ((minuteHandLengthRatio * 5) - handsPadding)));
    setNumberAttribute(minuteHand, 'y2', 5 - (Math.cos(minuteAngle) * ((minuteHandLengthRatio * 5) - handsPadding)));
    setNumberAttribute(hourHand, 'x1', 5 - (Math.sin(hourAngle) * (hourHandLengthRatio * handsTailLength)));
    setNumberAttribute(hourHand, 'y1', 5 + (Math.cos(hourAngle) * (hourHandLengthRatio * handsTailLength)));
    setNumberAttribute(hourHand, 'x2', 5 + (Math.sin(hourAngle) * ((hourHandLengthRatio * 5) - handsPadding)));
    setNumberAttribute(hourHand, 'y2', 5 - (Math.cos(hourAngle) * ((hourHandLengthRatio * 5) - handsPadding)));
    updateTimeout = schedule(tick, 1000);
  };

  secondHand.setAttribute('stroke-width', (thickness / 2).toString());
  minuteHand.setAttribute('stroke-width', thickness.toString());
  hourHand.setAttribute('stroke-width', thickness.toString());
  applySecondHandClass(false);
  calculateColors();
  tick();
  const themeChanged = () => calculateColors();
  window.addEventListener('misskey:theme-changed', themeChanged);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      window.removeEventListener('misskey:theme-changed', themeChanged);
      if (updateTimeout !== 0) window.clearTimeout(updateTimeout);
      for (const timeout of timeouts) window.clearTimeout(timeout);
      timeouts.clear();
    },
  };
}
