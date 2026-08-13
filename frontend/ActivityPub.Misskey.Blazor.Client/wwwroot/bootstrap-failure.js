(() => {
  'use strict';

  function fail(errorCode) {
    const root = document.getElementById('app');
    if (!(root instanceof HTMLElement)) return;

    const panel = document.createElement('main');
    panel.className = 'mk-initialization-error';
    panel.setAttribute('role', 'alert');
    panel.dataset.errorCode = String(errorCode);

    const title = document.createElement('h1');
    title.textContent = 'クライアントを安全に初期化できませんでした。';
    const description = document.createElement('p');
    description.textContent = '設定と認証サービスを確認してから、再読み込みしてください。';
    const reference = document.createElement('p');
    reference.className = 'mk-error-reference';
    reference.textContent = String(errorCode);
    const retry = document.createElement('button');
    retry.type = 'button';
    retry.textContent = '再読み込み';
    retry.addEventListener('click', () => window.location.reload(), { once: true });

    panel.append(title, description, reference, retry);
    root.replaceChildren(panel);
  }

  Object.defineProperty(window, 'activityPubFrontendBootstrap', {
    configurable: false,
    enumerable: false,
    writable: false,
    value: Object.freeze({ fail })
  });
})();
