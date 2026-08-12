const tooltipDelay = 300;

// Direct browser-lifecycle port of Misskey 12.119.2 scripts/use-tooltip.ts.
export function attach(target, receiver) {
    if (!(target instanceof HTMLButtonElement)) {
        throw new TypeError('RENOTE_BUTTON_TARGET_INVALID');
    }

    let disposed = false;
    let hovering = false;
    let shouldIgnoreMouseover = false;
    let showing = false;
    let timeout = 0;

    const invoke = method => receiver.invokeMethodAsync(method).catch(error => {
        if (!disposed && target.isConnected) {
            console.error('Renote button tooltip callback failed.', error);
        }
    });
    const clearTimer = () => {
        if (timeout !== 0) {
            window.clearTimeout(timeout);
            timeout = 0;
        }
    };
    const close = () => {
        clearTimer();
        if (!showing) return;
        showing = false;
        invoke('HideRenoteTooltipAsync');
    };
    const open = () => {
        timeout = 0;
        close();
        if (disposed || !hovering || !document.body.contains(target)) return;
        showing = true;
        invoke('ShowRenoteTooltipAsync');
    };
    const schedule = () => {
        clearTimer();
        timeout = window.setTimeout(open, tooltipDelay);
    };
    const onMouseover = () => {
        if (hovering || shouldIgnoreMouseover) return;
        hovering = true;
        schedule();
    };
    const onMouseleave = () => {
        if (!hovering) return;
        hovering = false;
        close();
    };
    const onTouchstart = () => {
        shouldIgnoreMouseover = true;
        if (hovering) return;
        hovering = true;
        schedule();
    };
    const onTouchend = () => {
        if (!hovering) return;
        hovering = false;
        close();
    };

    target.addEventListener('mouseover', onMouseover, { passive: true });
    target.addEventListener('mouseleave', onMouseleave, { passive: true });
    target.addEventListener('touchstart', onTouchstart, { passive: true });
    target.addEventListener('touchend', onTouchend, { passive: true });
    target.addEventListener('click', close, { passive: true });
    target.dataset.renoteButtonReady = 'true';

    hovering = target.matches(':hover');
    if (hovering) schedule();

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            close();
            target.removeEventListener('mouseover', onMouseover);
            target.removeEventListener('mouseleave', onMouseleave);
            target.removeEventListener('touchstart', onTouchstart);
            target.removeEventListener('touchend', onTouchend);
            target.removeEventListener('click', close);
            delete target.dataset.renoteButtonReady;
        },
    };
}
