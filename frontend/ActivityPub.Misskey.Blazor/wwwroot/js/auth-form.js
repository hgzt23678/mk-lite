function setBusy(form, busy) {
  form.classList.toggle('signing', busy);
  form.setAttribute('aria-busy', busy ? 'true' : 'false');
  for (const button of form.querySelectorAll('[data-auth-submit]')) {
    button.disabled = busy;
    const content = button.querySelector('.content');
    if (content) {
      content.textContent = busy
        ? button.dataset.pendingLabel ?? content.textContent
        : button.dataset.defaultLabel ?? content.textContent;
    }
  }
}

async function postFormTo(form, target, fields = {}) {
  const body = new FormData(form);
  for (const [name, value] of Object.entries(fields)) {
    if (value !== null && value !== undefined) body.set(name, value);
  }
  const response = await fetch(target, {
    method: 'POST',
    body,
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
    throw new Error('AUTH_RESPONSE_CONTENT_TYPE');
  }
  const payload = await response.json();
  return { response, payload };
}

async function postForm(form, fields = {}) {
  return postFormTo(form, form.action, fields);
}

// Misskey's native sign-in contract reports failures as { error: { id } },
// while the Blazor presentation layer uses deliberately non-sensitive local
// error codes. Keep the wire response untouched and translate only at this
// browser boundary so suspended, lockout, TOTP, and passkey states retain the
// same UI behavior as the upstream client.
function authenticationErrorCode(payload, fallback) {
  if (typeof payload?.errorCode === 'string' && payload.errorCode.length > 0) {
    return payload.errorCode;
  }

  const id = typeof payload?.error?.id === 'string' ? payload.error.id : '';
  return {
    '6cc579cc-885d-43d8-95c2-b8c7fc963280': 'INVALID_CREDENTIALS',
    '932c904e-9460-45b7-9ce6-7ed33be7eb2c': 'INVALID_CREDENTIALS',
    'e03a5f46-d309-4865-9b69-56282d94e1eb': 'ACCOUNT_NOT_ACTIVE',
    '22d05606-fbcf-421a-a2db-b32610dcfd1b': 'RATE_LIMIT_EXCEEDED',
    'cdf1235b-ac71-46d4-a3a6-84ccce48df6f': 'INVALID_TWO_FACTOR_CODE',
    'f27fd449-9af4-4841-9249-1f989b9fa4a4': 'PASSKEY_UNAVAILABLE',
    '93b86c4b-72f9-40eb-9815-798928603d1e': 'INVALID_PASSKEY_ASSERTION',
    '2715a88a-2125-4013-932f-aa6fe72792da': 'PASSKEY_STATE_CONFLICT',
  }[id] ?? fallback;
}

function decodeBase64Url(value) {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, character => character.charCodeAt(0));
}

function encodeBase64Url(value) {
  const bytes = new Uint8Array(value);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

function parsePasskeyRequestOptions(options) {
  if (typeof globalThis.PublicKeyCredential === 'function' &&
      typeof globalThis.PublicKeyCredential.parseRequestOptionsFromJSON === 'function') {
    return globalThis.PublicKeyCredential.parseRequestOptionsFromJSON(options);
  }

  return {
    ...options,
    challenge: decodeBase64Url(options.challenge),
    allowCredentials: Array.isArray(options.allowCredentials)
      ? options.allowCredentials.map(credential => ({
          ...credential,
          id: decodeBase64Url(credential.id),
        }))
      : undefined,
  };
}

function serializePasskeyCredential(credential) {
  if (typeof credential?.toJSON === 'function') {
    return JSON.stringify(credential.toJSON());
  }

  const response = credential?.response;
  if (typeof globalThis.PublicKeyCredential !== 'function' ||
      typeof globalThis.AuthenticatorAssertionResponse !== 'function' ||
      !(credential instanceof globalThis.PublicKeyCredential) ||
      !(response instanceof globalThis.AuthenticatorAssertionResponse)) {
    throw new Error('PASSKEY_ASSERTION_TYPE');
  }

  return JSON.stringify({
    id: credential.id,
    rawId: encodeBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: {
      authenticatorData: encodeBase64Url(response.authenticatorData),
      clientDataJSON: encodeBase64Url(response.clientDataJSON),
      signature: encodeBase64Url(response.signature),
      userHandle: response.userHandle ? encodeBase64Url(response.userHandle) : null,
    },
  });
}

function passwordStrength(source) {
  let power = 0.018;
  if (/[a-zA-Z]/.test(source) && /[0-9]/.test(source)) power += 0.020;
  if (/[a-z]/.test(source) && /[A-Z]/.test(source)) power += 0.015;
  if (/[!\x22#$%&@'()*+,\-./_]/.test(source)) power += 0.02;
  return Math.max(0, Math.min(1, power * source.length));
}

export function attachSignIn(form, receiver, passkeyOptionsUrl, passkeyAssertionUrl) {
  let disposed = false;
  let passkeyController = null;
  let savedUsername = '';
  let savedPassword = '';
  let hasSecurityKeys = false;

  const passwordInputFor = element =>
    element?.closest?.('.matxzzsk')?.querySelector('input[name="password"]') ?? null;

  const setCapsLockState = (input, enabled) => {
    const root = input.closest('.matxzzsk');
    const warning = root?.querySelector('[data-caps-lock-warning]');
    if (warning instanceof HTMLElement) warning.hidden = !enabled;
    input.dataset.capsLock = enabled ? 'on' : 'off';
  };

  const onPasswordKeyboard = event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || input.name !== 'password') return;
    setCapsLockState(input, event.getModifierState?.('CapsLock') === true);
  };

  const onFocusOut = event => {
    const input = event.target;
    if (input instanceof HTMLInputElement && input.name === 'password') {
      setCapsLockState(input, false);
    }
  };

  const onClick = event => {
    const button = event.target?.closest?.('[data-password-toggle]');
    if (!(button instanceof HTMLButtonElement) || !form.contains(button)) return;
    const input = passwordInputFor(button);
    if (!(input instanceof HTMLInputElement)) return;
    const selectionStart = input.selectionStart;
    const selectionEnd = input.selectionEnd;
    const reveal = input.type === 'password';
    input.type = reveal ? 'text' : 'password';
    button.setAttribute('aria-pressed', reveal ? 'true' : 'false');
    const icon = button.querySelector('.fas');
    icon?.classList.toggle('fa-eye', !reveal);
    icon?.classList.toggle('fa-eye-slash', reveal);
    input.focus({ preventScroll: true });
    if (selectionStart !== null && selectionEnd !== null) {
      input.setSelectionRange(selectionStart, selectionEnd);
    }
  };

  const restoreRetainedPassword = () => {
    if (!savedPassword) return;
    requestAnimationFrame(() => {
      if (disposed) return;
      const input = form.querySelector('input[name="password"]');
      if (input instanceof HTMLInputElement && input.value === '') input.value = savedPassword;
    });
  };

  const onInput = event => {
    if (disposed || !(event.target instanceof HTMLInputElement)) return;
    if (event.target.name === 'password') {
      savedPassword = event.target.value;
      return;
    }
    if (event.target.name !== 'username') return;
    savedUsername = event.target.value;
  };

  const completeSuccessfulAuthentication = async payload => {
    await receiver.invokeMethodAsync('NotifyAuthenticationSucceeded');
    // A successful callback closes the dialog and therefore disposes this attachment before
    // the close transition completes. That disposal is expected and must not suppress the
    // server-validated post-authentication navigation.
    window.location.assign(payload.redirectUrl);
  };

  const completePasskey = async requestOptions => {
    if (disposed || !hasSecurityKeys || !savedUsername || !savedPassword ||
        !navigator.credentials || typeof globalThis.PublicKeyCredential !== 'function') {
      setBusy(form, false);
      return;
    }
    passkeyController?.abort();
    passkeyController = new AbortController();
    setBusy(form, true);
    try {
      await receiver.invokeMethodAsync('NotifyPasskeyAvailable');
      await receiver.invokeMethodAsync('NotifyTwoFactorRequired');
      restoreRetainedPassword();
      await receiver.invokeMethodAsync('NotifyPasskeyQuerying', true);
      setBusy(form, false);
      const credential = await navigator.credentials.get({
        publicKey: parsePasskeyRequestOptions(requestOptions),
        signal: passkeyController.signal,
      });
      if (!credential) throw new Error('PASSKEY_ASSERTION_EMPTY');

      await receiver.invokeMethodAsync('NotifyPasskeyQuerying', false);
      setBusy(form, true);
      const assertion = await postFormTo(form, passkeyAssertionUrl, {
        credential: serializePasskeyCredential(credential),
      });
      if (assertion.response.ok && assertion.payload.status === 'succeeded' &&
          typeof assertion.payload.redirectUrl === 'string') {
        savedPassword = '';
        await completeSuccessfulAuthentication(assertion.payload);
        return;
      }

      setBusy(form, false);
      await receiver.invokeMethodAsync(
        'NotifyAuthenticationFailure',
        authenticationErrorCode(assertion.payload, 'PASSKEY_FAILED'));
      restoreRetainedPassword();
    } catch (error) {
      setBusy(form, false);
      await receiver.invokeMethodAsync('NotifyPasskeyQuerying', false);
      if (error?.name === 'NotAllowedError' || error?.name === 'AbortError' || disposed) return;
      await receiver.invokeMethodAsync('NotifyAuthenticationFailure', 'PASSKEY_FAILED');
      restoreRetainedPassword();
    }
  };

  const queryPasskey = async () => {
    if (disposed || !hasSecurityKeys || !savedUsername || !savedPassword) {
      setBusy(form, false);
      return;
    }
    try {
      const { response, payload: requestOptions } = await postFormTo(form, passkeyOptionsUrl, {
        username: savedUsername,
        password: savedPassword,
      });
      if (!response.ok) {
        setBusy(form, false);
        await receiver.invokeMethodAsync(
          'NotifyAuthenticationFailure',
          authenticationErrorCode(requestOptions, 'PASSKEY_FAILED'));
        restoreRetainedPassword();
        return;
      }

      await completePasskey(requestOptions);
    } catch {
      setBusy(form, false);
      if (!disposed) {
        await receiver.invokeMethodAsync('NotifyAuthenticationFailure', 'PASSKEY_FAILED');
        restoreRetainedPassword();
      }
    }
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (disposed || form.classList.contains('signing')) return;
    setBusy(form, true);
    const current = new FormData(form);
    const username = String(current.get('username') ?? savedUsername);
    const password = String(current.get('password') ?? savedPassword);
    if (username) savedUsername = username;
    if (password) savedPassword = password;
    try {
      const { payload } = await postForm(form, {
        username: savedUsername,
        password: savedPassword,
      });
      if (payload.status === 'succeeded' && typeof payload.redirectUrl === 'string') {
        savedPassword = '';
        await completeSuccessfulAuthentication(payload);
        return;
      }
      if (payload.status === 'two-factor-required') {
        setBusy(form, false);
        await receiver.invokeMethodAsync('NotifyTwoFactorRequired');
        restoreRetainedPassword();
        return;
      }
      if (payload.status === 'passkey-required' && payload.publicKey &&
          typeof payload.publicKey === 'object') {
        hasSecurityKeys = true;
        await completePasskey(payload.publicKey);
        return;
      }
      setBusy(form, false);
      await receiver.invokeMethodAsync('NotifyAuthenticationFailure', authenticationErrorCode(payload, 'SIGNIN_FAILED'));
      restoreRetainedPassword();
    } catch {
      setBusy(form, false);
      if (!disposed) {
        await receiver.invokeMethodAsync('NotifyAuthenticationFailure', 'SIGNIN_FAILED');
        restoreRetainedPassword();
      }
    }
  };

  form.addEventListener('input', onInput);
  form.addEventListener('submit', onSubmit);
  form.addEventListener('click', onClick);
  form.addEventListener('keydown', onPasswordKeyboard);
  form.addEventListener('keyup', onPasswordKeyboard);
  form.addEventListener('focusout', onFocusOut);
  return {
    retryPasskey() {
      return queryPasskey();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      savedPassword = '';
      savedUsername = '';
      passkeyController?.abort();
      form.removeEventListener('input', onInput);
      form.removeEventListener('submit', onSubmit);
      form.removeEventListener('click', onClick);
      form.removeEventListener('keydown', onPasswordKeyboard);
      form.removeEventListener('keyup', onPasswordKeyboard);
      form.removeEventListener('focusout', onFocusOut);
    },
  };
}

export function attachSignUp(form, receiver, usernameAvailabilityUrl) {
  let disposed = false;
  let usernameAvailabilityController = null;

  const onInput = async event => {
    if (disposed || !(event.target instanceof HTMLInputElement)) return;
    const input = event.target;
    if (input.name === 'username') {
      const username = input.value;
      usernameAvailabilityController?.abort();
      usernameAvailabilityController = new AbortController();
      if (!username) {
        await receiver.invokeMethodAsync('NotifyUsernameState', null);
        return;
      }
      const invalid = !/^[a-zA-Z0-9_]+$/.test(username)
        ? 'invalid-format'
        : username.length < 1
          ? 'min-range'
          : username.length > 20
            ? 'max-range'
            : null;
      if (invalid) {
        await receiver.invokeMethodAsync('NotifyUsernameState', invalid);
        return;
      }
      await receiver.invokeMethodAsync('NotifyUsernameState', 'wait');
      try {
        const response = await fetch(`${usernameAvailabilityUrl}?username=${encodeURIComponent(username)}`, {
          credentials: 'same-origin',
          cache: 'no-store',
          signal: usernameAvailabilityController.signal,
        });
        if (!response.ok) throw new Error('USERNAME_AVAILABILITY_FAILED');
        const result = await response.json();
        await receiver.invokeMethodAsync('NotifyUsernameState', result.available === true ? 'ok' : 'unavailable');
      } catch (error) {
        if (error?.name !== 'AbortError' && !disposed) {
          await receiver.invokeMethodAsync('NotifyUsernameState', 'error');
        }
      }
    } else if (input.name === 'email') {
      if (input.value === '') {
        await receiver.invokeMethodAsync('NotifyEmailState', null);
        return;
      }
      // Only syntax is evaluated before registration. A remote availability lookup would
      // disclose whether an address already owns an account.
      await receiver.invokeMethodAsync(
        'NotifyEmailState',
        input.validity.typeMismatch || input.value.length > 256 ? 'unavailable:format' : null);
    } else if (input.name === 'password') {
      const strength = passwordStrength(input.value);
      await receiver.invokeMethodAsync(
        'NotifyPasswordStrength',
        input.value === '' ? '' : strength > 0.7 ? 'high' : strength > 0.3 ? 'medium' : 'low');
      const retyped = form.elements.namedItem('retypedPassword');
      if (retyped instanceof HTMLInputElement && retyped.value !== '') {
        await receiver.invokeMethodAsync(
          'NotifyPasswordRetypeState',
          input.value === retyped.value ? 'match' : 'not-match');
      }
    } else if (input.name === 'retypedPassword') {
      const password = form.elements.namedItem('password');
      const state = input.value === ''
        ? null
        : password instanceof HTMLInputElement && password.value === input.value
          ? 'match'
          : 'not-match';
      await receiver.invokeMethodAsync('NotifyPasswordRetypeState', state);
    }
  };

  const onSubmit = async event => {
    event.preventDefault();
    if (disposed || form.classList.contains('signing')) return;
    const submittedEmail = String(new FormData(form).get('email') ?? '');
    setBusy(form, true);
    await receiver.invokeMethodAsync('NotifyRegistrationStarted');
    try {
      const { payload } = await postForm(form);
      if (payload.status === 'succeeded' && typeof payload.redirectUrl === 'string') {
        form.reset();
        await receiver.invokeMethodAsync('NotifyRegistrationSucceeded');
        window.location.assign(payload.redirectUrl);
        return;
      }
      if (payload.status === 'signup-email-pending') {
        form.reset();
        setBusy(form, false);
        await receiver.invokeMethodAsync('NotifyEmailPending', submittedEmail);
        return;
      }
      setBusy(form, false);
      form.dispatchEvent(new CustomEvent('misskey:captcha-reset'));
      await receiver.invokeMethodAsync('NotifyRegistrationFailure', payload.errorCode ?? 'REGISTRATION_FAILED');
    } catch {
      setBusy(form, false);
      form.dispatchEvent(new CustomEvent('misskey:captcha-reset'));
      if (!disposed) await receiver.invokeMethodAsync('NotifyRegistrationFailure', 'REGISTRATION_FAILED');
    }
  };

  form.addEventListener('input', onInput);
  form.addEventListener('submit', onSubmit);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      usernameAvailabilityController?.abort();
      form.removeEventListener('input', onInput);
      form.removeEventListener('submit', onSubmit);
    },
  };
}

export function attachInitialSetup(form, receiver) {
  let disposed = false;

  const onSubmit = async event => {
    event.preventDefault();
    if (disposed || form.dataset.submitting === 'true') return;
    if (!form.reportValidity()) return;

    const fields = new FormData(form);
    form.dataset.submitting = 'true';
    await receiver.invokeMethodAsync('NotifyInitialSetupStarted');
    try {
      const response = await fetch(form.action, {
        method: 'POST',
        credentials: 'same-origin',
        cache: 'no-store',
        redirect: 'error',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          'X-ActivityPub-Frontend': '1',
        },
        body: JSON.stringify({
          username: String(fields.get('username') ?? ''),
          password: String(fields.get('password') ?? ''),
        }),
      });
      const contentType = response.headers.get('content-type') ?? '';
      const payload = contentType.toLowerCase().startsWith('application/json')
        ? await response.json()
        : null;
      if (response.ok && typeof payload?.token === 'string') {
        form.reset();
        await receiver.invokeMethodAsync('NotifyInitialSetupSucceeded');
        window.location.assign('/');
        return;
      }

      form.dataset.submitting = 'false';
      await receiver.invokeMethodAsync(
        'NotifyInitialSetupFailure',
        typeof payload?.error?.code === 'string' ? payload.error.code : 'INITIAL_SETUP_FAILED');
    } catch {
      form.dataset.submitting = 'false';
      if (!disposed) await receiver.invokeMethodAsync('NotifyInitialSetupFailure', 'INITIAL_SETUP_FAILED');
    }
  };

  form.addEventListener('submit', onSubmit);
  // Static SSR can expose the form before the Interactive Server circuit has
  // attached its browser boundary. Publish readiness only after the listener
  // is installed so automation and assistive integrations do not race the
  // first submission.
  form.dataset.setupReady = 'true';
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      delete form.dataset.setupReady;
      form.removeEventListener('submit', onSubmit);
    },
  };
}
