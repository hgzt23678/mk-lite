export function prefersDark() {
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}
