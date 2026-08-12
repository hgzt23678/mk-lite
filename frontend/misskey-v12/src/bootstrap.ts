import { completeCallbackIfPresent, configureAuthentication, currentUser } from './activitypub-auth';
import { fetchRuntimeConfig } from './activitypub-runtime';

async function bootstrap(): Promise<void> {
  const language = chooseLanguage(_LANGS_.map(([key]) => key));
  const localeResponse = await fetch(`/app/assets/locales/${language}.${_VERSION_}.json`, {
    cache: 'no-cache',
    credentials: 'omit',
    redirect: 'error',
  });
  if (!localeResponse.ok) throw new Error(`Locale is unavailable (${localeResponse.status})`);
  localStorage.setItem('lang', language);
  localStorage.setItem('locale', await localeResponse.text());
  localStorage.setItem('localeVersion', _VERSION_);

  const config = await fetchRuntimeConfig();
  window.__ACTIVITYPUB_RUNTIME_CONFIG__ = config;
  configureAuthentication(config);
  await completeCallbackIfPresent();
  const user = await currentUser();
  if (user) {
    const response = await fetch('/api/i', {
      method: 'POST',
      headers: { Accept: 'application/json', Authorization: `Bearer ${user.access_token}` },
      cache: 'no-store',
      credentials: 'omit',
      redirect: 'error',
    });
    if (!response.ok) throw new Error(`Account projection is unavailable (${response.status})`);
    const account = await response.json() as Record<string, unknown>;
    account.token = 'oidc-session';
    localStorage.setItem('account', JSON.stringify(account));
  } else {
    localStorage.removeItem('account');
  }

  await import('./init');
}

function chooseLanguage(supported: string[]): string {
  const saved = localStorage.getItem('lang');
  if (saved && supported.includes(saved)) return saved;
  if (supported.includes(navigator.language)) return navigator.language;
  return supported.find(language => language.split('-')[0] === navigator.language.split('-')[0]) ?? 'en-US';
}

bootstrap().catch(error => {
  console.error(error);
  document.body.replaceChildren();
  const message = document.createElement('p');
  message.setAttribute('role', 'alert');
  message.textContent = 'クライアントを安全に初期化できませんでした。設定と認証サービスを確認してください。';
  document.body.append(message);
});
