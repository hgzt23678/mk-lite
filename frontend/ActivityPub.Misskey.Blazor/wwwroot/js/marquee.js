export function setDuration(content, repeat, requestedDuration) {
  if (!(content instanceof HTMLElement)) {
    throw new TypeError('The marquee content element is unavailable.');
  }
  if (!Number.isInteger(repeat) || repeat < 1) {
    throw new RangeError('The marquee repeat count must be positive.');
  }
  if (!Number.isFinite(requestedDuration) || requestedDuration < 0) {
    throw new RangeError('The marquee duration must be a finite non-negative number.');
  }

  // This is the pinned Misskey 12.119.2 calculation: a 3000px run takes the requested duration.
  const eachLength = content.offsetWidth / repeat;
  const calculated = eachLength === 0
    ? 0
    : requestedDuration / ((1 / eachLength) * 3000);
  content.style.animationDuration = `${calculated}s`;
  return calculated;
}
