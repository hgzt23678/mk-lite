export function observe(receiver) {
    let disposed = false;
    let frame = 0;
    const publish = () => {
        frame = 0;
        if (!disposed) receiver.invokeMethodAsync('UpdateViewport', window.innerWidth);
    };
    const onResize = () => {
        if (frame === 0) frame = window.requestAnimationFrame(publish);
    };

    window.addEventListener('resize', onResize, { passive: true });
    publish();
    return {
        dispose() {
            disposed = true;
            window.removeEventListener('resize', onResize);
            if (frame !== 0) window.cancelAnimationFrame(frame);
        },
    };
}
