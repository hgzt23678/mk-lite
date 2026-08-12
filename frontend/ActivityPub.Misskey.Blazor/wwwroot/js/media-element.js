export function attachVolume(element, initialVolume, receiver) {
    if (!(element instanceof HTMLMediaElement)) throw new TypeError('An audio or video element is required.');
    if (!Number.isFinite(initialVolume) || initialVolume < 0 || initialVolume > 1) {
        throw new RangeError('The initial media volume is invalid.');
    }

    let disposed = false;
    const onVolumeChange = () => {
        receiver.invokeMethodAsync('StoreVolumeAsync', element.volume).catch(error => {
            if (!disposed && element.isConnected) console.error('Media volume persistence failed.', error);
        });
    };
    element.addEventListener('volumechange', onVolumeChange, { passive: true });
    element.volume = initialVolume;

    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            element.removeEventListener('volumechange', onVolumeChange);
        },
    };
}
