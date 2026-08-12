export function attach(root, header, body, initialParentTop, receiver) {
    const offsetChangedEvent = 'misskey:sticky-offset-changed';
    let disposed = false;
    let frame = 0;
    let timeout = 0;
    let parentTop = finite(initialParentTop);

    const apply = () => {
        frame = 0;
        if (disposed || !root.isConnected) return;
        const inheritedTop = Number.parseFloat(
            getComputedStyle(header).getPropertyValue('--stickyTop'));
        if (Number.isFinite(inheritedTop)) parentTop = finite(inheritedTop);
        const height = header.offsetHeight;
        header.style.position = 'sticky';
        header.style.top = 'var(--stickyTop, 0)';
        header.style.zIndex = '1000';
        body.dataset.stickyContainerHeaderHeight = `${height}`;
        body.style.setProperty('--stickyTop', `${parentTop + height}px`);
        receiver.invokeMethodAsync('UpdateStickyHeaderHeight', height, parentTop)
            .catch(error => {
                if (!disposed && root.isConnected) {
                    console.error('Sticky container callback failed.', error);
                }
            });
        window.dispatchEvent(new CustomEvent(offsetChangedEvent, { detail: { body } }));
    };
    const schedule = () => {
        if (frame !== 0) window.cancelAnimationFrame(frame);
        if (timeout !== 0) window.clearTimeout(timeout);
        timeout = window.setTimeout(() => {
            timeout = 0;
            frame = window.requestAnimationFrame(apply);
        }, 100);
    };

    const observer = new ResizeObserver(schedule);
    const onAncestorOffsetChanged = event => {
        const changedBody = event.detail?.body;
        if (changedBody instanceof Element && changedBody !== body && changedBody.contains(root)) {
            apply();
        }
    };
    observer.observe(header);
    window.addEventListener(offsetChangedEvent, onAncestorOffsetChanged);
    apply();

    return {
        setParentTop(value) {
            parentTop = finite(value);
            apply();
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            observer.disconnect();
            window.removeEventListener(offsetChangedEvent, onAncestorOffsetChanged);
            if (timeout !== 0) window.clearTimeout(timeout);
            if (frame !== 0) window.cancelAnimationFrame(frame);
        },
    };
}

function finite(value) {
    return Number.isFinite(value) ? Math.max(0, value) : 0;
}
