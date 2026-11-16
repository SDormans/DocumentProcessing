// Echo the server-issued anti-forgery token on state-changing requests (see AntiforgerySetup on the API).
export const xsrfHeaders = (): Record<string, string> => {
  const match = document.cookie.split('; ').find((cookie) => cookie.startsWith('XSRF-TOKEN='));
  return match ? { 'X-XSRF-TOKEN': decodeURIComponent(match.substring('XSRF-TOKEN='.length)) } : {};
};
