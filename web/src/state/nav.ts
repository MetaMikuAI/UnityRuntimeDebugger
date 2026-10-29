import { computed, signal } from '@preact/signals';
import { isMobile, pane } from './ui';

export interface NavEntry {
  handle: number;
  title: string;
}

const MaxHistory = 50;

/** Inspector history: back/forward like a browser. */
export const history = signal<NavEntry[]>([]);
export const position = signal(-1);
export const current = computed(() => history.value[position.value] ?? null);

export function inspect(handle: number, title = '$' + handle) {
  const kept = history.value.slice(0, position.value + 1);
  if (kept[kept.length - 1]?.handle !== handle) kept.push({ handle, title });
  history.value = kept.slice(-MaxHistory);
  position.value = history.value.length - 1;
  if (isMobile.value) pane.value = 'main';
}

export function go(index: number) {
  if (index >= 0 && index < history.value.length) position.value = index;
}

/** The inspector names an entry once it has loaded it. */
export function retitle(handle: number, title: string) {
  if (history.value.some(e => e.handle === handle && e.title !== title))
    history.value = history.value.map(e => (e.handle === handle ? { ...e, title } : e));
}

export function resetNav() {
  history.value = [];
  position.value = -1;
}
