import PhotoSwipeLightbox from '../vendor/photoswipe/photoswipe-lightbox.esm.min.js';
import PhotoSwipe from '../vendor/photoswipe/photoswipe.esm.min.js';

function normalizeItem(value) {
  if (!value || typeof value !== 'object') throw new TypeError('PhotoSwipe media item is required.');
  if (typeof value.id !== 'string' || typeof value.src !== 'string' || typeof value.msrc !== 'string') {
    throw new TypeError('PhotoSwipe media item identifiers and URLs are required.');
  }
  const width = Number(value.width);
  const height = Number(value.height);
  return Object.freeze({
    id: value.id,
    src: value.src,
    msrc: value.msrc,
    width: Number.isFinite(width) && width > 0 ? width : 1,
    height: Number.isFinite(height) && height > 0 ? height : 1,
    alt: typeof value.alt === 'string' ? value.alt : '',
  });
}

export function attach(gallery, sourceItems) {
  if (!(gallery instanceof HTMLElement)) throw new TypeError('MkMediaList requires a gallery element.');
  if (!Array.isArray(sourceItems) || sourceItems.length === 0) {
    throw new TypeError('MkMediaList requires at least one image item.');
  }

  const items = sourceItems.map(normalizeItem);
  const byId = new Map(items.map(item => [item.id, item]));
  const padding = window.innerWidth > 500
    ? { top: 32, bottom: 32, left: 32, right: 32 }
    : { top: 0, bottom: 0, left: 0, right: 0 };
  const lightbox = new PhotoSwipeLightbox({
    dataSource: items.map(item => ({
      id: item.id,
      src: item.src,
      msrc: item.msrc,
      w: item.width,
      h: item.height,
      alt: item.alt,
    })),
    gallery,
    children: '.image',
    thumbSelector: '.image',
    loop: false,
    padding,
    imageClickAction: 'close',
    tapAction: 'toggle-controls',
    pswpModule: PhotoSwipe,
  });

  lightbox.on('itemData', event => {
    const element = event.itemData.element;
    const id = element instanceof HTMLElement ? element.dataset.id : event.itemData.id;
    const item = typeof id === 'string' ? byId.get(id) : undefined;
    if (!item) return;
    event.itemData.id = item.id;
    event.itemData.src = item.src;
    event.itemData.w = item.width;
    event.itemData.h = item.height;
    event.itemData.msrc = item.msrc;
    event.itemData.alt = item.alt;
    event.itemData.thumbCropped = true;
  });
  lightbox.init();

  let disposed = false;
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      lightbox.destroy();
      byId.clear();
    },
  };
}
