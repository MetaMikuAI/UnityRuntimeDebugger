import { computed, signal } from '@preact/signals';
import { ApiError, api } from '../api/client';
import type { Status } from '../api/types';
import { resetNav } from './nav';
import { toast } from './ui';

export const status = signal<Status | null>(null);
export const connection = signal<'connecting' | 'ok' | 'busy' | 'offline'>('connecting');
export const boot = computed(() => status.value?.debugger.boot ?? null);

const bootListeners: ((boot: string) => void)[] = [];

/** Runs when the game restarts: handles from the previous run are meaningless. */
export function onBoot(listener: (boot: string) => void) {
  bootListeners.push(listener);
}

let timer: ReturnType<typeof setTimeout> | undefined;

export async function pollStatus() {
  clearTimeout(timer);
  try {
    const next = await api.status();
    const previous = status.value?.debugger.boot;
    status.value = next;
    connection.value = next.mainThread ? 'ok' : 'busy';
    if (previous && previous !== next.debugger.boot) {
      resetNav();
      bootListeners.forEach(l => l(next.debugger.boot));
      toast('游戏已重启，旧的对象句柄已失效');
    } else if (!previous) {
      bootListeners.forEach(l => l(next.debugger.boot));
    }
  } catch (e) {
    connection.value = e instanceof ApiError && e.status === 401 ? 'connecting' : 'offline';
    if (e instanceof ApiError && e.status === 401) return;
  }
  timer = setTimeout(pollStatus, document.hidden ? 6000 : 2000);
}

export function stopStatus() {
  clearTimeout(timer);
}
