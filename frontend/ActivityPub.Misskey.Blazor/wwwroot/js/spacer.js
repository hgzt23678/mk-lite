function resolveDeviceKind(overriddenDeviceKind) {
    if (overriddenDeviceKind === 'smartphone' ||
        overriddenDeviceKind === 'tablet' ||
        overriddenDeviceKind === 'desktop') {
        return overriddenDeviceKind;
    }

    const userAgent = navigator.userAgent.toLowerCase();
    const tablet = /ipad/.test(userAgent) ||
        (/mobile|iphone|android/.test(userAgent) && window.innerWidth > 700);
    const smartphone = !tablet && /mobile|iphone|android/.test(userAgent);
    return smartphone ? 'smartphone' : tablet ? 'tablet' : 'desktop';
}

export function observe(element, options, receiver) {
    let disposed = false;
    let frame = 0;
    const deviceKind = resolveDeviceKind(options?.overriddenDeviceKind);

    const publish = () => {
        frame = 0;
        if (!disposed && element.isConnected) {
            receiver.invokeMethodAsync(
                'UpdateSpacer',
                element.offsetWidth,
                window.innerWidth,
                deviceKind)
                .catch(error => {
                    if (!disposed && element.isConnected) {
                        console.error('Spacer size callback failed.', error);
                    }
                });
        }
    };
    const schedule = () => {
        if (frame === 0) {
            frame = window.requestAnimationFrame(publish);
        }
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
            if (frame !== 0) {
                window.cancelAnimationFrame(frame);
            }
        },
    };
}
