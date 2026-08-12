const tooltipDelay = 100;

// Browser lifecycle port of Misskey 12.119.2 scripts/use-tooltip.ts and the
// v-ripple directive used by MkReactionsViewer.reaction.vue.
export function attach(target, receiver, initialCanToggle) {
    if (!(target instanceof HTMLButtonElement)) {
        throw new TypeError('REACTION_VIEWER_TARGET_INVALID');
    }

    let disposed = false;
    let hovering = false;
    let shouldIgnoreMouseover = false;
    let opened = false;
    let canToggle = initialCanToggle === true;
    let timeout = 0;

    const invoke = (method, ...args) => receiver.invokeMethodAsync(method, ...args).catch(error => {
        if (!disposed && target.isConnected) {
            console.error('Reaction viewer callback failed.', error);
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
        if (!opened) return;
        opened = false;
        invoke('HideReactionTooltipAsync');
    };
    const open = () => {
        timeout = 0;
        close();
        if (disposed || !hovering || !target.isConnected) return;
        opened = true;
        invoke('ShowReactionTooltipAsync');
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
    const onClick = () => {
        if (canToggle) {
            const rect = target.getBoundingClientRect();
            invoke(
                'ShowReactionRippleAsync',
                rect.left + (target.offsetWidth / 2),
                rect.top + (target.offsetHeight / 2));
        }
        close();
    };

    target.addEventListener('mouseover', onMouseover, { passive: true });
    target.addEventListener('mouseleave', onMouseleave, { passive: true });
    target.addEventListener('touchstart', onTouchstart, { passive: true });
    target.addEventListener('touchend', onTouchend, { passive: true });
    target.addEventListener('click', onClick, { passive: true });
    target.dataset.reactionViewerReady = 'true';

    hovering = target.matches(':hover');
    if (hovering) schedule();

    return {
        setCanToggle(value) {
            canToggle = value === true;
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            clearTimer();
            target.removeEventListener('mouseover', onMouseover);
            target.removeEventListener('mouseleave', onMouseleave);
            target.removeEventListener('touchstart', onTouchstart);
            target.removeEventListener('touchend', onTouchend);
            target.removeEventListener('click', onClick);
            delete target.dataset.reactionViewerReady;
        },
    };
}
