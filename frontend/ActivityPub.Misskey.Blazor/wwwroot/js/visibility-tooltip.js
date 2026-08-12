import { claimZIndex } from './overlay-stack.js';

const tooltipDelay = 300;

export function attachTrigger(target, receiver) {
    if (!(target instanceof HTMLElement)) throw new TypeError('A visibility tooltip target is required.');

    let disposed = false;
    let timer = 0;
    let hovering = false;
    let focused = false;
    let touching = false;
    let shouldIgnoreMouseover = false;

    const invoke = name => receiver.invokeMethodAsync(name).catch(error => {
        if (!disposed && target.isConnected) {
            console.error('Visibility tooltip callback failed.', error);
        }
    });
    const clearTimer = () => {
        if (timer !== 0) {
            window.clearTimeout(timer);
            timer = 0;
        }
    };
    const isActive = () => hovering || focused || touching;
    const open = () => {
        timer = 0;
        if (!disposed && isActive() && target.isConnected) {
            invoke('ShowVisibilityTooltipAsync');
        }
    };
    const schedule = delay => {
        clearTimer();
        timer = window.setTimeout(open, delay);
    };
    const closeIfInactive = () => {
        clearTimer();
        if (!isActive()) invoke('HideVisibilityTooltipAsync');
    };
    const onMouseover = () => {
        if (hovering || shouldIgnoreMouseover) return;
        hovering = true;
        schedule(tooltipDelay);
    };
    const onMouseleave = () => {
        hovering = false;
        closeIfInactive();
    };
    const onTouchstart = () => {
        shouldIgnoreMouseover = true;
        touching = true;
        schedule(tooltipDelay);
    };
    const onTouchend = () => {
        touching = false;
        closeIfInactive();
    };
    const onFocus = () => {
        focused = true;
        schedule(tooltipDelay);
    };
    const onBlur = () => {
        focused = false;
        closeIfInactive();
    };
    const onClick = event => {
        if (event.detail !== 0) {
            hovering = false;
            touching = false;
            closeIfInactive();
        }
    };
    const onKeydown = event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            focused = false;
            clearTimer();
            invoke('HideVisibilityTooltipAsync');
        } else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            focused = true;
            schedule(0);
        }
    };

    target.addEventListener('mouseover', onMouseover, { passive: true });
    target.addEventListener('mouseleave', onMouseleave, { passive: true });
    target.addEventListener('touchstart', onTouchstart, { passive: true });
    target.addEventListener('touchend', onTouchend, { passive: true });
    target.addEventListener('touchcancel', onTouchend, { passive: true });
    target.addEventListener('focus', onFocus, { passive: true });
    target.addEventListener('blur', onBlur, { passive: true });
    target.addEventListener('click', onClick, { passive: true });
    target.addEventListener('keydown', onKeydown);
    target.dataset.visibilityTooltipReady = 'true';

    // Interactive Server hydration can finish after the pointer or keyboard focus has
    // already reached the statically rendered target.  Native mouseover/focus does not
    // fire again in that case, so recover the current state when attaching.
    hovering = target.matches(':hover');
    focused = document.activeElement === target;
    if (isActive()) schedule(tooltipDelay);

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            clearTimer();
            target.removeEventListener('mouseover', onMouseover);
            target.removeEventListener('mouseleave', onMouseleave);
            target.removeEventListener('touchstart', onTouchstart);
            target.removeEventListener('touchend', onTouchend);
            target.removeEventListener('touchcancel', onTouchend);
            target.removeEventListener('focus', onFocus);
            target.removeEventListener('blur', onBlur);
            target.removeEventListener('click', onClick);
            target.removeEventListener('keydown', onKeydown);
            delete target.dataset.visibilityTooltipReady;
        },
    };
}

function splitTimes(value) {
    return value.split(',').map(part => {
        const normalized = part.trim();
        if (normalized.endsWith('ms')) return Number.parseFloat(normalized) || 0;
        if (normalized.endsWith('s')) return (Number.parseFloat(normalized) || 0) * 1000;
        return 0;
    });
}

function maximumTransitionTime(element) {
    const style = getComputedStyle(element);
    const durations = splitTimes(style.transitionDuration);
    const delays = splitTimes(style.transitionDelay);
    const length = Math.max(durations.length, delays.length);
    let maximum = 0;
    for (let index = 0; index < length; index += 1) {
        maximum = Math.max(
            maximum,
            durations[index % durations.length] + delays[index % delays.length]);
    }
    return maximum;
}

function normalizeTooltipOptions(target, value) {
    const direction = ['top', 'bottom', 'left', 'right'].includes(value?.direction)
        ? value.direction
        : 'top';
    const innerMargin = Number.isFinite(value?.innerMargin) ? value.innerMargin : 0;
    const x = Number.isFinite(value?.x) ? value.x : null;
    const y = Number.isFinite(value?.y) ? value.y : null;
    if (!(target instanceof HTMLElement) && (x === null || y === null)) {
        throw new TypeError('A tooltip target or finite x and y coordinates are required.');
    }
    return {
        direction,
        innerMargin,
        x,
        y,
        animation: value?.animation !== false,
    };
}

function positionTooltip(target, tooltip, options) {
    const rect = target?.getBoundingClientRect();
    const contentWidth = tooltip.offsetWidth;
    const contentHeight = tooltip.offsetHeight;
    const anchorWidth = target?.offsetWidth ?? 0;
    const anchorHeight = target?.offsetHeight ?? 0;
    const anchorX = target ? rect.left + window.pageXOffset : options.x;
    const anchorY = target ? rect.top + window.pageYOffset : options.y;

    const topPosition = () => {
        let left = anchorX + (target ? anchorWidth / 2 : 0) - (contentWidth / 2);
        const top = anchorY - contentHeight - options.innerMargin;
        if (left + contentWidth - window.pageXOffset > window.innerWidth) {
            left = window.innerWidth - contentWidth + window.pageXOffset - 1;
        }
        return { left, top };
    };
    const bottomPosition = () => {
        let left = anchorX + (target ? anchorWidth / 2 : 0) - (contentWidth / 2);
        const top = anchorY + (target ? anchorHeight : 0) + options.innerMargin;
        if (left + contentWidth - window.pageXOffset > window.innerWidth) {
            left = window.innerWidth - contentWidth + window.pageXOffset - 1;
        }
        return { left, top };
    };
    const leftPosition = () => {
        const left = anchorX - contentWidth - options.innerMargin;
        let top = anchorY + (target ? anchorHeight / 2 : 0) - (contentHeight / 2);
        if (top + contentHeight - window.pageYOffset > window.innerHeight) {
            top = window.innerHeight - contentHeight + window.pageYOffset - 1;
        }
        return { left, top };
    };
    const rightPosition = () => {
        const left = anchorX + (target ? anchorWidth : 0) + options.innerMargin;
        let top = anchorY + (target ? anchorHeight / 2 : 0) - (contentHeight / 2);
        if (top + contentHeight - window.pageYOffset > window.innerHeight) {
            top = window.innerHeight - contentHeight + window.pageYOffset - 1;
        }
        return { left, top };
    };

    let position;
    let transformOrigin;
    if (options.direction === 'bottom') {
        position = bottomPosition();
        transformOrigin = 'center top';
    } else if (options.direction === 'left') {
        position = leftPosition();
        if (position.left - window.pageXOffset < 0) {
            position = rightPosition();
            transformOrigin = 'left center';
        } else {
            transformOrigin = 'right center';
        }
    } else if (options.direction === 'right') {
        position = rightPosition();
        transformOrigin = 'left center';
    } else {
        position = topPosition();
        if (position.top - window.pageYOffset < 0) {
            position = bottomPosition();
            transformOrigin = 'center top';
        } else {
            transformOrigin = 'center bottom';
        }
    }

    tooltip.style.left = `${position.left}px`;
    tooltip.style.top = `${position.top}px`;
    tooltip.style.transformOrigin = transformOrigin;
}

export function attachTooltip(target, tooltip, receiver, suppliedOptions = {}) {
    const anchor = target instanceof HTMLElement ? target : null;
    if (!(tooltip instanceof HTMLElement)) throw new TypeError('A tooltip element is required.');
    const options = normalizeTooltipOptions(anchor, suppliedOptions);

    let disposed = false;
    let showing = false;
    let positionFrame = 0;
    let enterFrame = 0;
    let completionTimer = 0;
    let transitionHandler = null;
    tooltip.style.zIndex = String(claimZIndex('high'));

    const notifyClosed = () => receiver.invokeMethodAsync('NotifyTooltipClosedAsync').catch(error => {
        if (!disposed && tooltip.isConnected) {
            console.error('Visibility tooltip close callback failed.', error);
        }
    });

    const cancelCompletion = () => {
        if (completionTimer !== 0) {
            window.clearTimeout(completionTimer);
            completionTimer = 0;
        }
        if (transitionHandler !== null) {
            tooltip.removeEventListener('transitionend', transitionHandler);
            transitionHandler = null;
        }
    };
    const startPositionLoop = () => {
        const update = () => {
            if (disposed || !showing || (anchor && !anchor.isConnected) || !tooltip.isConnected) {
                positionFrame = 0;
                return;
            }
            positionTooltip(anchor, tooltip, options);
            positionFrame = window.requestAnimationFrame(update);
        };
        if (positionFrame === 0) update();
    };
    const stopPositionLoop = () => {
        if (positionFrame !== 0) {
            window.cancelAnimationFrame(positionFrame);
            positionFrame = 0;
        }
    };
    const show = () => {
        if (disposed) return;
        cancelCompletion();
        showing = true;
        tooltip.hidden = false;
        positionTooltip(anchor, tooltip, options);
        startPositionLoop();
        if (!options.animation) {
            tooltip.classList.remove(
                'tooltip-enter-active',
                'tooltip-enter-from',
                'tooltip-leave-active');
            tooltip.dataset.tooltipState = 'shown';
            return;
        }
        tooltip.dataset.tooltipState = 'entering';
        tooltip.classList.remove('tooltip-leave-active');
        tooltip.classList.add('tooltip-enter-active', 'tooltip-enter-from');
        if (enterFrame !== 0) window.cancelAnimationFrame(enterFrame);
        enterFrame = window.requestAnimationFrame(() => {
            enterFrame = window.requestAnimationFrame(() => {
                enterFrame = 0;
                if (disposed || !showing) return;
                tooltip.classList.remove('tooltip-enter-from');
                tooltip.dataset.tooltipState = 'shown';
                const duration = maximumTransitionTime(tooltip);
                completionTimer = window.setTimeout(() => {
                    completionTimer = 0;
                    tooltip.classList.remove('tooltip-enter-active');
                }, duration + 34);
            });
        });
    };
    const hide = () => {
        if (disposed || !showing) return;
        showing = false;
        stopPositionLoop();
        cancelCompletion();
        if (!options.animation) {
            tooltip.hidden = true;
            tooltip.dataset.tooltipState = 'closed';
            void notifyClosed();
            return;
        }
        tooltip.classList.remove('tooltip-enter-active', 'tooltip-enter-from');
        tooltip.classList.add('tooltip-leave-active');
        tooltip.dataset.tooltipState = 'leaving';
        const finish = () => {
            if (disposed || showing) return;
            cancelCompletion();
            tooltip.hidden = true;
            tooltip.dataset.tooltipState = 'closed';
            void notifyClosed();
        };
        const expectedProperties = new Set(['opacity', 'transform']);
        transitionHandler = event => {
            if (event.target !== tooltip) return;
            expectedProperties.delete(event.propertyName);
            if (expectedProperties.size === 0) finish();
        };
        tooltip.addEventListener('transitionend', transitionHandler);
        completionTimer = window.setTimeout(finish, maximumTransitionTime(tooltip) + 50);
    };

    show();
    return {
        show,
        hide,
        dispose() {
            if (disposed) return;
            disposed = true;
            showing = false;
            stopPositionLoop();
            cancelCompletion();
            if (enterFrame !== 0) window.cancelAnimationFrame(enterFrame);
        },
    };
}
