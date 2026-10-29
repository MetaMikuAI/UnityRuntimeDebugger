import type { Value } from '../api/types';

export const fmtMb = (mb: number) => (mb >= 1024 ? (mb / 1024).toFixed(1) + ' GB' : Math.round(mb) + ' MB');

export const fmtTime = (ms: number) => {
  const d = new Date(ms);
  return d.toLocaleTimeString('zh-CN', { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
};

export const fmtDuration = (us: number) => (us >= 1000 ? (us / 1000).toFixed(us >= 100000 ? 0 : 1) + ' ms' : us.toFixed(us >= 100 ? 0 : 1) + ' µs');

export const lastSegment = (name = '') => name.slice(name.lastIndexOf('.') + 1);

/** Plain text of a value, for copying. */
export function valueText(v?: Value): string {
  if (!v) return 'null';
  switch (v.kind) {
    case 'null': return 'null';
    case 'void': return '';
    case 'string': case 'char': case 'enum': case 'type': return v.text ?? '';
    case 'struct': return v.text ?? v.type ?? '';
    case 'object': return v.text ? `${v.type} ${v.text}` : v.type ?? '';
    default: return v.text ?? v.kind;
  }
}

/** Whether a value opens into members (strings show their text in place instead). */
export const canExpand = (v?: Value) => !!v?.handle && v.kind !== 'string' && v.kind !== 'destroyed';

export function copyText(text: string) {
  return navigator.clipboard?.writeText(text).catch(() => fallbackCopy(text)) ?? fallbackCopy(text);
}

function fallbackCopy(text: string) {
  const area = document.createElement('textarea');
  area.value = text;
  document.body.append(area);
  area.select();
  document.execCommand('copy');
  area.remove();
  return Promise.resolve();
}

export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms: number) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return (...args: A) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}
