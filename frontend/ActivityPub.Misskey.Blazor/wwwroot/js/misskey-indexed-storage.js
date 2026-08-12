const databaseName = 'activitypub-misskey-v12';
const storeName = 'kv';
const fallbackPrefix = 'idbfallback::';

let databasePromise;
let indexedDbAvailable = typeof indexedDB !== 'undefined';

function database() {
  if (!indexedDbAvailable) return Promise.resolve(null);
  if (!databasePromise) {
    databasePromise = new Promise(resolve => {
      const request = indexedDB.open(databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => {
        indexedDbAvailable = false;
        resolve(null);
      };
    });
  }
  return databasePromise;
}

export async function get(key) {
  const db = await database();
  if (!db) return window.localStorage.getItem(fallbackPrefix + key);
  return new Promise((resolve, reject) => {
    const request = db.transaction(storeName, 'readonly').objectStore(storeName).get(key);
    request.onsuccess = () => resolve(request.result ?? null);
    request.onerror = () => reject(request.error ?? new Error('indexeddb-read-failed'));
  });
}

export async function set(key, value) {
  const db = await database();
  if (!db) {
    window.localStorage.setItem(fallbackPrefix + key, value);
    return;
  }
  await new Promise((resolve, reject) => {
    const request = db.transaction(storeName, 'readwrite').objectStore(storeName).put(value, key);
    request.onsuccess = resolve;
    request.onerror = () => reject(request.error ?? new Error('indexeddb-write-failed'));
  });
}

export async function del(key) {
  const db = await database();
  if (!db) {
    window.localStorage.removeItem(fallbackPrefix + key);
    return;
  }
  await new Promise((resolve, reject) => {
    const request = db.transaction(storeName, 'readwrite').objectStore(storeName).delete(key);
    request.onsuccess = resolve;
    request.onerror = () => reject(request.error ?? new Error('indexeddb-delete-failed'));
  });
}
