const freshSessionParameter = 'freshSession';
const freshSessionValue = 'localtest';
const localTestClientStateKeys = ['irondev.token', 'irondev.tenantId', 'irondev.selectedProjectId'];

export function applyLocalTestFreshSessionFromUrl() {
  const parameters = new URLSearchParams(window.location.search);
  if (parameters.get(freshSessionParameter) !== freshSessionValue) {
    return;
  }

  if (!isLocalTestSessionScope()) {
    return;
  }

  for (const key of localTestClientStateKeys) {
    window.localStorage.removeItem(key);
    window.sessionStorage.removeItem(key);
  }

  parameters.delete(freshSessionParameter);
  const query = parameters.toString();
  const nextUrl = `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`;
  window.history.replaceState(window.history.state, document.title, nextUrl);
}

function isLocalTestSessionScope() {
  if (import.meta.env.MODE !== 'localtest') {
    return false;
  }

  const host = window.location.hostname.toLowerCase();
  return host === '127.0.0.1' || host === 'localhost';
}
