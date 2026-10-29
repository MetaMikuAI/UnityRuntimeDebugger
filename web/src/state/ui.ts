import { effect, signal } from '@preact/signals';
import type { ComponentChildren } from 'preact';
import { ApiError, describe } from '../api/client';
import { store } from '../lib/store';

// ---- theme --------------------------------------------------------------------------------

export type Theme = 'dark' | 'light';
const prefersLight = typeof matchMedia !== 'undefined' && matchMedia('(prefers-color-scheme: light)').matches;
export const theme = signal<Theme>(store.get('theme', prefersLight ? 'light' : 'dark'));
effect(() => {
  document.documentElement.dataset.theme = theme.value;
  store.set('theme', theme.value);
});

// ---- layout -------------------------------------------------------------------------------

export type Pane = 'side' | 'main' | 'drawer';
export type SideTab = 'scene' | 'types' | 'session';
export type DrawerTab = 'console' | 'logs' | 'hooks';

const mobileQuery = matchMedia('(max-width: 720px)');
export const isMobile = signal(mobileQuery.matches);
mobileQuery.addEventListener('change', e => (isMobile.value = e.matches));

/** On phones one pane is visible at a time. */
export const pane = signal<Pane>('side');
export const sideTab = signal<SideTab>(store.get('sideTab', 'scene'));
export const drawerTab = signal<DrawerTab>(store.get('drawerTab', 'console'));
export const drawerMax = signal(false);
export const sizes = signal(store.get('sizes', { side: 300, drawer: 300 }));
effect(() => store.set('sideTab', sideTab.value));
effect(() => store.set('drawerTab', drawerTab.value));
effect(() => store.set('sizes', sizes.value));

export function showDrawer(tab: DrawerTab) {
  drawerTab.value = tab;
  if (isMobile.value) pane.value = 'drawer';
}

// ---- toasts -------------------------------------------------------------------------------

export interface Toast {
  id: number;
  text: string;
  kind: '' | 'ok' | 'err';
}

export const toasts = signal<Toast[]>([]);
let toastId = 0;

export function toast(text: string, kind: Toast['kind'] = '') {
  const id = ++toastId;
  toasts.value = [...toasts.value.slice(-4), { id, text, kind }];
  setTimeout(() => (toasts.value = toasts.value.filter(t => t.id !== id)), kind === 'err' ? 5000 : 2600);
}

/** Reports an error, except "unauthorized", which already shows the login screen. */
export function fail(e: unknown) {
  if (e instanceof ApiError && e.status === 401) return;
  if ((e as Error)?.name === 'AbortError') return;
  toast(describe(e), 'err');
}

// ---- context menu & modal -----------------------------------------------------------------

export type MenuItem = { label: string; icon?: string; hint?: string; action: () => void } | 'separator';

export const menu = signal<{ x: number; y: number; items: MenuItem[] } | null>(null);

export function openMenu(event: MouseEvent, items: MenuItem[]) {
  event.preventDefault();
  event.stopPropagation();
  menu.value = { x: event.clientX, y: event.clientY, items };
}

export interface ModalContent {
  title: string;
  body: ComponentChildren;
  actions?: ComponentChildren;
  wide?: boolean;
}

export const modal = signal<ModalContent | null>(null);

export const paletteOpen = signal(false);
