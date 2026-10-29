import { effect, signal } from '@preact/signals';
import type { Value } from '../api/types';
import { store } from '../lib/store';
import { onBoot } from './status';

export interface Pin {
  handle: number;
  label: string;
  value: Value;
}

/** Pins point at handles, which only live as long as the game run (boot). */
export const pins = signal<Pin[]>([]);
let pinBoot: string | null = null;

onBoot(boot => {
  const saved = store.get<{ boot: string; pins: Pin[] } | null>('pins', null, 'session');
  pins.value = saved?.boot === boot ? saved.pins : [];
  pinBoot = boot;
});
effect(() => {
  if (pinBoot) store.set('pins', { boot: pinBoot, pins: pins.value }, 'session');
});

export function pin(value: Value, label: string) {
  if (!value.handle || pins.value.some(p => p.handle === value.handle)) return;
  pins.value = [...pins.value, { handle: value.handle, label, value }];
}

export function unpin(handle: number) {
  pins.value = pins.value.filter(p => p.handle !== handle);
}

/** Watch expressions survive restarts: they are code, not handles. */
export const watches = signal<string[]>(store.get('watches', []));
effect(() => store.set('watches', watches.value));

export function addWatch(expression: string) {
  const trimmed = expression.trim();
  if (trimmed && !watches.value.includes(trimmed)) watches.value = [...watches.value, trimmed];
}

export const snippets = signal<{ name: string; code: string }[]>(store.get('snippets', []));
effect(() => store.set('snippets', snippets.value));
