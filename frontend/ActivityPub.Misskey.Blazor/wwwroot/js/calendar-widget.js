export function attach(element, receiver) {
  if (!(element instanceof HTMLElement) || !receiver) {
    throw new Error('MISSKEY_CALENDAR_WIDGET_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let visible = true;
  let timer = 0;
  let frame = 0;
  let observer = null;

  const clearSchedule = () => {
    if (timer !== 0) {
      window.clearTimeout(timer);
      timer = 0;
    }
    if (frame !== 0) {
      window.cancelAnimationFrame(frame);
      frame = 0;
    }
  };

  const canUpdate = () => !disposed && visible && !document.hidden && element.isConnected;

  const snapshot = () => {
    const now = new Date();
    const year = now.getFullYear();
    const monthIndex = now.getMonth();
    const day = now.getDate();
    const dayStart = new Date(year, monthIndex, day).getTime();
    const monthStart = new Date(year, monthIndex, 1).getTime();
    const nextMonth = new Date(year, monthIndex + 1, 1).getTime();
    const yearStart = new Date(year, 0, 1).getTime();
    const nextYear = new Date(year + 1, 0, 1).getTime();
    const current = now.getTime();
    return {
      year,
      month: monthIndex + 1,
      day,
      weekDay: now.getDay(),
      dayProgress: ((current - dayStart) / 86_400_000) * 100,
      monthProgress: ((current - monthStart) / (nextMonth - monthStart)) * 100,
      yearProgress: ((current - yearStart) / (nextYear - yearStart)) * 100,
    };
  };

  const schedule = () => {
    if (!canUpdate()) return;
    timer = window.setTimeout(() => {
      timer = 0;
      publish();
    }, 1_000);
  };

  const publish = () => {
    clearSchedule();
    if (!canUpdate()) return;
    frame = window.requestAnimationFrame(() => {
      frame = 0;
      if (!canUpdate()) return;
      receiver.invokeMethodAsync('UpdateCalendar', snapshot())
        .catch(error => {
          if (!disposed && element.isConnected) {
            console.error('Misskey calendar widget callback failed.', error);
          }
        })
        .finally(schedule);
    });
  };

  const onVisibilityChanged = () => {
    if (document.hidden) clearSchedule();
    else publish();
  };

  document.addEventListener('visibilitychange', onVisibilityChanged);
  if ('IntersectionObserver' in globalThis) {
    observer = new IntersectionObserver(entries => {
      const nextVisible = entries.at(-1)?.isIntersecting ?? false;
      if (visible === nextVisible) return;
      visible = nextVisible;
      if (visible) publish();
      else clearSchedule();
    });
    observer.observe(element);
  }

  publish();

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      clearSchedule();
      observer?.disconnect();
      document.removeEventListener('visibilitychange', onVisibilityChanged);
    },
  };
}
