export function observe(element, receiver) {
    let disposed = false;
    let frame = 0;
    const publish = () => {
        frame = 0;
        if (!disposed && element.isConnected) {
            receiver.invokeMethodAsync('UpdateElementSize', element.getBoundingClientRect().width, window.innerWidth)
                .catch(error => {
                    if (!disposed && element.isConnected) console.error('Element size callback failed.', error);
                });
        }
    };
    const schedule = () => {
        if (frame === 0) frame = window.requestAnimationFrame(publish);
    };
    const observer = new ResizeObserver(schedule);
    observer.observe(element);
    window.addEventListener('resize', schedule, { passive: true });
    publish();

    return {
        dispose() {
            disposed = true;
            observer.disconnect();
            window.removeEventListener('resize', schedule);
            if (frame !== 0) window.cancelAnimationFrame(frame);
        },
    };
}
