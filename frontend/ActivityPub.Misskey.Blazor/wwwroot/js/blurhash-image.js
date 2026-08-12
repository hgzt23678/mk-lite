import decode from './vendor/blurhash-1.1.5.js';

export function draw(canvas, image, hash, size) {
    const imageLoaded = image instanceof HTMLImageElement && image.complete && image.naturalWidth > 0;
    // An immediately cached image can fire load and remove the placeholder before
    // the server-side interop invocation reaches the browser.
    if (!(canvas instanceof HTMLCanvasElement)) {
        if (imageLoaded) return true;
        throw new TypeError('A BlurHash canvas is required.');
    }
    if (!Number.isInteger(size) || size < 1 || size > 512) throw new RangeError('The BlurHash canvas size is invalid.');
    if (hash != null) {
        const pixels = decode(hash, size, size);
        const context = canvas.getContext('2d');
        if (context === null) throw new Error('The BlurHash 2D canvas context is unavailable.');
        const imageData = context.createImageData(size, size);
        imageData.data.set(pixels);
        context.putImageData(imageData, 0, 0);
    }

    return imageLoaded;
}
