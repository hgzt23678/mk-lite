/* Keycloak presentation adapter for Misskey 12.119.2. Never reads or logs field values. */
(() => {
  'use strict';

  const setHostSuffixes = () => {
    for (const suffix of document.querySelectorAll('[data-host-suffix]')) {
      suffix.textContent = `@${window.location.hostname}`;
    }
  };

  const attachFocusState = () => {
    for (const input of document.querySelectorAll('.matxzzsk > .input > input')) {
      const container = input.closest('.input');
      if (!container) continue;
      input.addEventListener('focus', () => container.classList.add('focused'));
      input.addEventListener('blur', () => container.classList.remove('focused'));
      if (document.activeElement === input) container.classList.add('focused');
    }
  };

  const attachPasswordToggles = () => {
    for (const button of document.querySelectorAll('[data-mk-password-toggle]')) {
      button.addEventListener('click', () => {
        const controlId = button.getAttribute('aria-controls');
        const input = controlId ? document.getElementById(controlId) : null;
        if (!(input instanceof HTMLInputElement)) return;

        const showing = input.type === 'text';
        input.type = showing ? 'password' : 'text';
        button.setAttribute('aria-pressed', String(!showing));
        button.setAttribute(
          'aria-label',
          showing ? button.dataset.labelShow ?? '' : button.dataset.labelHide ?? ''
        );
        const icon = button.querySelector('.fas');
        icon?.classList.toggle('fa-eye', showing);
        icon?.classList.toggle('fa-eye-slash', !showing);
        input.focus({ preventScroll: true });
      });
    }
  };

  const attachSubmitState = () => {
    for (const form of document.querySelectorAll('form.eppvobhk')) {
      form.addEventListener('submit', event => {
        const submitter = event.submitter instanceof HTMLElement
          ? event.submitter
          : form.querySelector('[name="login"]');
        if (!(submitter instanceof HTMLButtonElement) || submitter.disabled) return;

        form.classList.add('signing');
        submitter.disabled = true;
        submitter.setAttribute('aria-disabled', 'true');
        const content = submitter.querySelector('.content');
        if (content && submitter.dataset.pendingLabel) {
          content.textContent = submitter.dataset.pendingLabel;
        }
      });
    }
  };

  const attachRipples = () => {
    for (const button of document.querySelectorAll('.bghgjjyj')) {
      button.addEventListener('pointerdown', event => {
        if (!(button instanceof HTMLElement) || button.matches(':disabled')) return;
        const host = button.querySelector('.ripples');
        if (!(host instanceof HTMLElement)) return;

        const bounds = button.getBoundingClientRect();
        const diameter = Math.hypot(bounds.width, bounds.height) * 2;
        const ripple = document.createElement('span');
        ripple.className = 'mk-keycloak-ripple';
        ripple.style.width = `${diameter}px`;
        ripple.style.height = `${diameter}px`;
        ripple.style.left = `${event.clientX - bounds.left - diameter / 2}px`;
        ripple.style.top = `${event.clientY - bounds.top - diameter / 2}px`;
        host.append(ripple);
        window.setTimeout(() => ripple.remove(), 500);
      }, { passive: true });
    }
  };

  const initialize = () => {
    setHostSuffixes();
    attachFocusState();
    attachPasswordToggles();
    attachSubmitState();
    attachRipples();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize, { once: true });
  } else {
    initialize();
  }
})();
