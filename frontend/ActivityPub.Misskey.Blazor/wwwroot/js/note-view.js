const ignoredInputs = 'input, textarea, [contenteditable]';

function hasModifiers(event, shift = false) {
    return event.ctrlKey || event.altKey || event.metaKey || event.shiftKey !== shift;
}

function actionFor(event) {
    if (hasModifiers(event, event.code === 'Tab' && event.shiftKey)) return null;
    if (event.code === 'KeyR') return 'reply';
    if (event.code === 'KeyE' || event.code === 'KeyA' || event.code === 'NumpadAdd' || event.code === 'Semicolon') return 'react';
    if (event.code === 'KeyQ') return 'renote';
    if (event.code === 'ArrowUp' || event.code === 'KeyK' || (event.code === 'Tab' && event.shiftKey)) return 'previous';
    if (event.code === 'ArrowDown' || event.code === 'KeyJ' || event.code === 'Tab') return 'next';
    if (event.code === 'Escape') return 'blur';
    if (event.code === 'KeyM' || event.code === 'KeyO') return 'menu';
    if (event.code === 'KeyS') return 'toggle-content';
    return null;
}

function focusSibling(element, direction) {
    let candidate = direction < 0 ? element.previousElementSibling : element.nextElementSibling;
    while (candidate !== null && !candidate.hasAttribute('tabindex')) {
        candidate = direction < 0 ? candidate.previousElementSibling : candidate.nextElementSibling;
    }
    candidate?.focus();
}

export function attach(element, receiver) {
    let disposed = false;
    const keydown = event => {
        if (disposed || document.activeElement?.matches(ignoredInputs)) return;
        const action = actionFor(event);
        if (action === null) return;
        event.preventDefault();
        event.stopPropagation();
        if (action === 'previous') {
            focusSibling(element, -1);
        } else if (action === 'next') {
            focusSibling(element, 1);
        } else if (action === 'blur') {
            element.blur();
        } else {
            void receiver.invokeMethodAsync('HandleNoteHotkeyAsync', action);
        }
    };
    const contextmenu = event => {
        if (disposed) return;
        event.stopPropagation();
        if (event.target instanceof Element && event.target.closest('a') !== null) return;
        if (window.getSelection()?.toString() !== '') return;
        event.preventDefault();
        void receiver.invokeMethodAsync('ShowNoteContextMenuAsync');
    };
    element.addEventListener('keydown', keydown);
    element.addEventListener('contextmenu', contextmenu);
    return {
        focus() {
            if (!disposed) element.focus();
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            element.removeEventListener('keydown', keydown);
            element.removeEventListener('contextmenu', contextmenu);
        },
    };
}
