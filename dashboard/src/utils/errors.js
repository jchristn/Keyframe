// Turn an ApiError into localized, operator-facing text plus retry guidance. Server messages
// are shown as supporting detail; the headline is always a stable translated string keyed by
// status code, so the UI never depends on English-only server text.

export function errorHeadlineKey(err) {
  if (!err) return 'errors.unknown';
  if (err.network) return 'errors.network';
  switch (err.status) {
    case 400:
      return 'errors.badRequest';
    case 401:
      return 'errors.unauthorized';
    case 403:
      return 'errors.forbidden';
    case 404:
      return 'errors.notFound';
    case 409:
      return 'errors.conflict';
    case 422:
      return 'errors.unprocessable';
    case 429:
      return 'errors.tooManyRequests';
    case 502:
      return 'errors.badGateway';
    case 504:
      return 'errors.timeout';
    default:
      return err.status >= 500 ? 'errors.server' : 'errors.unknown';
  }
}

export function errorHintKey(err) {
  if (!err) return null;
  if (err.network) return 'errors.hints.network';
  switch (err.status) {
    case 400:
      return 'errors.hints.badRequest';
    case 403:
      return 'errors.hints.forbidden';
    case 422:
      return 'errors.hints.unprocessable';
    case 429:
      return 'errors.hints.tooManyRequests';
    case 502:
      return 'errors.hints.badGateway';
    case 504:
      return 'errors.hints.timeout';
    default:
      return null;
  }
}

/** One-line message for toasts: localized headline, then server message if any. */
export function describeError(t, err) {
  const head = t(errorHeadlineKey(err), { status: err && err.status });
  const msg = err && !err.network && err.message ? err.message : '';
  return msg && msg !== head ? head + ' ' + msg : head;
}
