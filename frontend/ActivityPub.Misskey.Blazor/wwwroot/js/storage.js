function area(name) {
    if (name === 'local') return window.localStorage;
    if (name === 'session') return window.sessionStorage;
    throw new TypeError('Unknown client storage area.');
}

export function read(areaName, key) {
    return area(areaName).getItem(key);
}

export function write(areaName, key, json) {
    area(areaName).setItem(key, json);
}

export function remove(areaName, key) {
    area(areaName).removeItem(key);
}
