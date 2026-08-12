// Port of Misskey 12.119.2 components/MkButton.vue ripple lifecycle.
export function attach(element, autofocus = false) {
    if (!(element instanceof HTMLElement)) {
        throw new TypeError('BUTTON_RIPPLE_TARGET_INVALID');
    }

    const ripples = element.querySelector(':scope > .ripples');
    if (!(ripples instanceof HTMLElement)) {
        throw new TypeError('BUTTON_RIPPLE_CONTAINER_MISSING');
    }

    const timeouts = new Set();
    let disposed = false;
    const schedule = (callback, delay) => {
        const id = window.setTimeout(() => {
            timeouts.delete(id);
            callback();
        }, delay);
        timeouts.add(id);
    };
    const onMouseDown = event => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }
        const rect = target.getBoundingClientRect();
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;
        const scale = Math.max(
            Math.hypot(x, y),
            Math.hypot(target.clientWidth - x, y),
            Math.hypot(x, target.clientHeight - y),
            Math.hypot(target.clientWidth - x, target.clientHeight - y));
        const ripple = document.createElement('div');
        ripple.style.top = `${y - 1}px`;
        ripple.style.left = `${x - 1}px`;
        ripples.appendChild(ripple);
        schedule(() => { ripple.style.transform = `scale(${scale})`; }, 1);
        schedule(() => {
            ripple.style.transition = 'all 1s ease';
            ripple.style.opacity = '0';
        }, 1000);
        schedule(() => ripple.remove(), 2000);
    };

    element.addEventListener('mousedown', onMouseDown);
    if (autofocus) {
        queueMicrotask(() => {
            if (!disposed && element.isConnected) {
                element.focus();
            }
        });
    }
    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            element.removeEventListener('mousedown', onMouseDown);
            for (const id of timeouts) window.clearTimeout(id);
            timeouts.clear();
            ripples.replaceChildren();
        },
    };
}
