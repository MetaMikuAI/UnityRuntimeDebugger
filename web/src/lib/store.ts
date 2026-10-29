// Per-browser conveniences (token, layout, snippets). Storage can be unavailable (private mode,
// blocked site data), so every access is guarded and the UI works without it.

type Kind = 'local' | 'session';

function area(kind: Kind): Storage | null {
  try {
    return kind === 'local' ? window.localStorage : window.sessionStorage;
  } catch {
    return null;
  }
}

export const store = {
  get<T>(key: string, fallback: T, kind: Kind = 'local'): T {
    try {
      const raw = area(kind)?.getItem('urdbg.' + key);
      return raw == null ? fallback : (JSON.parse(raw) as T);
    } catch {
      return fallback;
    }
  },
  set(key: string, value: unknown, kind: Kind = 'local') {
    try {
      area(kind)?.setItem('urdbg.' + key, JSON.stringify(value));
    } catch {
      /* storage unavailable */
    }
  },
  del(key: string, kind: Kind = 'local') {
    try {
      area(kind)?.removeItem('urdbg.' + key);
    } catch {
      /* storage unavailable */
    }
  },
};
