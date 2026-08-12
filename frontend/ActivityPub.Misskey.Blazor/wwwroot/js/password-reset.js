function setBusy(form, busy) {
  form.classList.toggle('signing', busy);
  for (const button of form.querySelectorAll('[data-password-reset-submit]')) {
    button.disabled = busy;
    const content = button.querySelector('.content');
    if (content) {
      content.textContent = busy
        ? button.dataset.pendingLabel ?? content.textContent
        : button.dataset.defaultLabel ?? content.textContent;
    }
  }
}

async function postForm(form) {
  const response = await fetch(form.action, {
    method: 'POST',
    body: new FormData(form),
    credentials: 'same-origin',
    cache: 'no-store',
    redirect: 'error',
    headers: {
      Accept: 'application/json',
      'X-ActivityPub-Frontend': '1',
    },
  });
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.toLowerCase().startsWith('application/json')) {
    throw new Error('PASSWORD_RESET_RESPONSE_CONTENT_TYPE');
  }
  return { response, payload: await response.json() };
}

function takeLocationFragment() {
  const fragment = window.location.hash.length > 1
    ? window.location.hash.substring(1)
    : '';
  window.history.replaceState(window.history.state, '', `${window.location.pathname}${window.location.search}`);
  return fragment;
}

function notifyReceiver(receiver, method, ...args) {
  // Fragment-only navigation can race an interactive-server component disposal.
  // A rejected DotNet invocation must not become an unhandled browser promise.
  void receiver.invokeMethodAsync(method, ...args).catch(() => {});
}

export function attachRequest(form, receiver) {
  let disposed = false;
  const onSubmit = async event => {
    event.preventDefault();
    if (disposed || form.classList.contains('signing')) return;
    setBusy(form, true);
    try {
      const { response, payload } = await postForm(form);
      if (response.status === 202 && payload.status === 'accepted') {
        form.reset();
        await receiver.invokeMethodAsync('NotifyRequestAccepted');
        return;
      }
      setBusy(form, false);
      await receiver.invokeMethodAsync(
        'NotifyRequestFailure',
        response.status === 429 ? 'RATE_LIMIT_EXCEEDED' : 'PASSWORD_RESET_REQUEST_FAILED');
    } catch {
      setBusy(form, false);
      if (!disposed) await receiver.invokeMethodAsync('NotifyRequestFailure', 'PASSWORD_RESET_REQUEST_FAILED');
    }
  };
  form.addEventListener('submit', onSubmit);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const input of form.querySelectorAll('input')) input.value = '';
      form.removeEventListener('submit', onSubmit);
    },
  };
}

export function attachCompletion(form, receiver) {
  let disposed = false;
  const hiddenToken = form.elements.namedItem('resetToken');
  const consumeFragment = () => {
    const fragment = takeLocationFragment();
    if (hiddenToken instanceof HTMLInputElement) hiddenToken.value = '';
    if (!(hiddenToken instanceof HTMLInputElement) ||
        fragment.length < 32 || fragment.length > 8192 || !/^[a-zA-Z0-9_-]+$/.test(fragment)) {
      queueMicrotask(() => {
        if (!disposed) notifyReceiver(receiver, 'NotifyMissingToken');
      });
      return;
    }

    hiddenToken.value = fragment;
    queueMicrotask(() => {
      if (!disposed) notifyReceiver(receiver, 'NotifyTokenReady');
    });
  };
  const onHashChange = () => consumeFragment();
  window.addEventListener('hashchange', onHashChange);
  consumeFragment();

  const onSubmit = async event => {
    event.preventDefault();
    if (disposed || form.classList.contains('signing') || !hiddenToken.value) return;
    setBusy(form, true);
    try {
      const { response, payload } = await postForm(form);
      if (response.ok && payload.status === 'succeeded' && typeof payload.redirectUrl === 'string') {
        hiddenToken.value = '';
        const password = form.elements.namedItem('password');
        if (password instanceof HTMLInputElement) password.value = '';
        await receiver.invokeMethodAsync('NotifyResetSucceeded');
        window.location.assign(payload.redirectUrl);
        return;
      }
      setBusy(form, false);
      await receiver.invokeMethodAsync('NotifyResetFailure', payload.errorCode ?? 'PASSWORD_RESET_FAILED');
    } catch {
      setBusy(form, false);
      if (!disposed) await receiver.invokeMethodAsync('NotifyResetFailure', 'PASSWORD_RESET_FAILED');
    }
  };
  form.addEventListener('submit', onSubmit);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      if (hiddenToken instanceof HTMLInputElement) hiddenToken.value = '';
      const password = form.elements.namedItem('password');
      if (password instanceof HTMLInputElement) password.value = '';
      form.removeEventListener('submit', onSubmit);
      window.removeEventListener('hashchange', onHashChange);
    },
  };
}

export function attachEmailConfirmation(form, receiver) {
  let disposed = false;
  let submitting = false;
  const hiddenToken = form.elements.namedItem('confirmationToken');
  const consumeFragment = () => {
    const fragment = takeLocationFragment();
    if (hiddenToken instanceof HTMLInputElement) hiddenToken.value = '';
    if (!(hiddenToken instanceof HTMLInputElement) ||
        fragment.length < 32 || fragment.length > 8192 || !/^[a-zA-Z0-9_-]+$/.test(fragment)) {
      queueMicrotask(() => {
        if (!disposed) notifyReceiver(receiver, 'NotifyMissingToken');
      });
      return;
    }

    hiddenToken.value = fragment;
    queueMicrotask(() => {
      if (!disposed) notifyReceiver(receiver, 'NotifyConfirmationReady');
    });
  };
  const onHashChange = () => consumeFragment();
  window.addEventListener('hashchange', onHashChange);
  consumeFragment();

  const confirm = async () => {
      if (disposed || submitting || !(hiddenToken instanceof HTMLInputElement) || !hiddenToken.value) return;
      submitting = true;
      try {
        const { response, payload } = await postForm(form);
        if (response.ok && payload.status === 'succeeded' && typeof payload.redirectUrl === 'string') {
          hiddenToken.value = '';
          return payload.redirectUrl;
        }
        submitting = false;
        await receiver.invokeMethodAsync('NotifyConfirmationFailure', payload.errorCode ?? 'INVALID_OR_EXPIRED_TOKEN');
      } catch {
        submitting = false;
        if (!disposed) await receiver.invokeMethodAsync('NotifyConfirmationFailure', 'EMAIL_CONFIRMATION_FAILED');
      }
      return null;
  };

  return {
    confirm() {
      return confirm();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (hiddenToken instanceof HTMLInputElement) hiddenToken.value = '';
      window.removeEventListener('hashchange', onHashChange);
    },
  };
}
