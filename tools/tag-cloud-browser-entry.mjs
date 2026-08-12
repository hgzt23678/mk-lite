import tinycolor from '../frontend/misskey-v12/node_modules/tinycolor2/esm/tinycolor.js';

const TAG_CANVAS_SOURCE = '/client-assets/tagcanvas.min.js';

function loadTagCanvas() {
  return new Promise(resolve => {
    if (typeof window.TagCanvas !== 'undefined') {
      resolve(true);
      return;
    }
    const script = document.createElement('script');
    script.async = true;
    script.src = TAG_CANVAS_SOURCE;
    script.addEventListener('load', () => resolve(typeof window.TagCanvas !== 'undefined'));
    script.addEventListener('error', () => resolve(false));
    document.head.appendChild(script);
  });
}

export async function attach(canvasId, tagsId, rootElement) {
  let disposed = false;
  const available = await loadTagCanvas();
  if (disposed || !available) {
    return { update() {}, dispose() {} };
  }

  const canvas = document.getElementById(canvasId);
  const tags = document.getElementById(tagsId);
  if (!(canvas instanceof HTMLCanvasElement) || !(tags instanceof HTMLElement)) {
    return { update() {}, dispose() {} };
  }

  const width = rootElement instanceof HTMLElement ? rootElement.offsetWidth : 300;
  canvas.width = width;
  const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent');
  window.TagCanvas.Start(canvasId, tagsId, {
    textColour: '#ffffff',
    outlineColour: tinycolor(accent).toHexString(),
    outlineRadius: 10,
    initial: [-0.03, -0.01],
    frontSelect: true,
    imageRadius: 8,
    dragThreshold: 3,
    wheelZoom: false,
    reverse: true,
    depth: 0.5,
    maxSpeed: 0.2,
    minSpeed: 0.003,
    stretchX: 0.8,
    stretchY: 0.8,
  });

  return {
    update() {
      if (window.TagCanvas) window.TagCanvas.Update(canvasId);
    },
    dispose() {
      disposed = true;
      if (window.TagCanvas) window.TagCanvas.Delete(canvasId);
    },
  };
}
