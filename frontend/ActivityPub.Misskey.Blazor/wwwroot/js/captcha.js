const providers = {
  hcaptcha: {
    variable: 'hcaptcha',
    source: 'https://js.hcaptcha.com/1/api.js?render=explicit&recaptchacompat=off',
  },
  recaptcha: {
    variable: 'grecaptcha',
    source: 'https://www.google.com/recaptcha/api.js?render=explicit',
  },
};

const scriptLoads = new Map();

function loadProvider(provider) {
  const definition = providers[provider];
  if (!definition) return Promise.reject(new Error('CAPTCHA_PROVIDER_INVALID'));
  if (globalThis[definition.variable]?.render) return Promise.resolve(globalThis[definition.variable]);
  const existing = scriptLoads.get(provider);
  if (existing) return existing;

  const promise = new Promise((resolve, reject) => {
    let script = document.getElementById(provider);
    const loaded = () => {
      const api = globalThis[definition.variable];
      if (api?.render) resolve(api);
      else reject(new Error('CAPTCHA_API_MISSING'));
    };
    const failed = () => reject(new Error('CAPTCHA_SCRIPT_FAILED'));
    if (!script) {
      script = document.createElement('script');
      script.async = true;
      script.id = provider;
      script.src = definition.source;
    }
    script.addEventListener('load', loaded, { once: true });
    script.addEventListener('error', failed, { once: true });
    if (!script.isConnected) document.head.appendChild(script);
  });
  scriptLoads.set(provider, promise);
  return promise;
}

export async function attachCaptcha(root, receiver, provider, siteKey, darkMode) {
  if (!(root instanceof HTMLElement) || !providers[provider] || typeof siteKey !== 'string' || !siteKey) {
    throw new Error('CAPTCHA_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let widgetId = null;
  let api = null;
  const container = root.querySelector('[data-captcha-container]');
  const responseInput = root.querySelector('[data-captcha-response]');
  if (!(container instanceof HTMLElement) || !(responseInput instanceof HTMLInputElement)) {
    throw new Error('CAPTCHA_DOM_INVALID');
  }

  const setResponse = async response => {
    if (disposed) return;
    responseInput.value = typeof response === 'string' ? response : '';
    await receiver.invokeMethodAsync('NotifyResponseChanged', responseInput.value.length > 0);
  };
  const reset = () => {
    responseInput.value = '';
    for (const submit of form?.querySelectorAll('[data-auth-submit]') ?? []) {
      if (submit instanceof HTMLButtonElement) submit.disabled = true;
    }
    if (api?.reset) api.reset(widgetId ?? undefined);
    if (!disposed) void receiver.invokeMethodAsync('NotifyResponseChanged', false);
  };
  const form = root.closest('form');
  form?.addEventListener('misskey:captcha-reset', reset);

  try {
    api = await loadProvider(provider);
    if (disposed) return { reset() {}, dispose() {} };
    await receiver.invokeMethodAsync('NotifyAvailable');
    widgetId = api.render(container, {
      sitekey: siteKey,
      theme: darkMode ? 'dark' : 'light',
      callback: response => void setResponse(response),
      'expired-callback': () => void setResponse(null),
      'error-callback': () => void setResponse(null),
    });
  } catch {
    await setResponse(null);
  }

  return {
    reset,
    dispose() {
      if (disposed) return;
      disposed = true;
      responseInput.value = '';
      form?.removeEventListener('misskey:captcha-reset', reset);
      if (api?.remove && widgetId !== null) api.remove(widgetId);
      else if (api?.reset) api.reset(widgetId ?? undefined);
      container.replaceChildren();
    },
  };
}
