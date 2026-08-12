import { attach as attachElement } from './form-suspense.js';

export function attach(container, receiver, generation, phase) {
  if (!(container instanceof HTMLElement) || !(container.firstElementChild instanceof HTMLElement)) {
    throw new Error('MISSKEY_NOTE_PAGE_TRANSITION_TARGET_MISSING');
  }

  return attachElement(container.firstElementChild, receiver, generation, phase);
}
