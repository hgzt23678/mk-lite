export function attach(textarea, receiver) {
  let disposed = false;

  const onKeyDown = event => {
    if (event.key !== 'Enter' || !event.ctrlKey) return;
    event.preventDefault();
    event.stopPropagation();
    receiver.invokeMethodAsync('NotifyCtrlEnter').catch(() => {});
  };

  textarea.addEventListener('keydown', onKeyDown);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      textarea.removeEventListener('keydown', onKeyDown);
    },
  };
}
