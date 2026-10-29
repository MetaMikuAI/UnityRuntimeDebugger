import { signal } from '@preact/signals';
import { store } from '../lib/store';
import type {
  Assembly, Call, Completion, ComponentEntry, EvalResult, Hook, HookAction, Inspection, ItemPage, LogEntry,
  Member, Method, MethodCandidate, Node, Page, Scene, Status, TypeInfo, TypeRow, Value, Variable, WatchResult,
} from './types';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly help?: string,
    readonly details?: Record<string, unknown>,
  ) {
    super(message);
  }
}

/** The token: from the link's #token=, the login form, or storage. Null shows the login screen. */
export const token = signal<string | null>(store.get('token', null, 'session') ?? store.get('token', null));

export function signIn(value: string, remember: boolean) {
  store.set('token', value, 'session');
  if (remember) store.set('token', value);
  else store.del('token');
  token.value = value;
}

export function signOut() {
  store.del('token', 'session');
  store.del('token');
  token.value = null;
}

type Query = Record<string, string | number | boolean | undefined | null>;

async function request<T>(method: string, path: string, options: { query?: Query; body?: unknown; as?: 'json' | 'text' | 'blob'; signal?: AbortSignal } = {}): Promise<T> {
  let url = path;
  if (options.query) {
    const params = new URLSearchParams();
    for (const [k, v] of Object.entries(options.query)) if (v != null && v !== false) params.set(k, String(v));
    if ([...params].length) url += '?' + params;
  }

  let response: Response;
  try {
    response = await fetch(url, {
      method,
      headers: { Authorization: `Bearer ${token.value ?? ''}`, ...(options.body !== undefined && { 'Content-Type': 'application/json' }) },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      cache: 'no-store',
      signal: options.signal,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new ApiError(0, 'offline', '无法连接到游戏');
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    const error = body.error ?? {};
    if (response.status === 401) token.value = null;
    throw new ApiError(response.status, error.code ?? 'http_' + response.status, error.message ?? response.statusText, error.help, error.details);
  }

  if (options.as === 'text') return (await response.text()) as T;
  if (options.as === 'blob') return (await response.blob()) as T;
  return response.json();
}

const get = <T>(path: string, query?: Query, signal?: AbortSignal) => request<T>('GET', path, { query, signal });
const post = <T>(path: string, body?: unknown) => request<T>('POST', path, { body: body ?? {} });
const enc = encodeURIComponent;

export const api = {
  status: () => get<Status>('/api/status'),

  scenes: () => get<Scene[]>('/api/scenes'),
  search: (q: string) => get<Node[]>('/api/objects/search', { q, limit: 300 }),
  children: (h: number) => get<Node[]>(`/api/objects/${h}/children`),
  components: (h: number) => get<ComponentEntry[]>(`/api/objects/${h}/components`),

  inspect: (h: number, statics = false) => get<Inspection>(`/api/objects/${h}`, { statics: statics ? 1 : undefined }),
  member: (h: number, name: string) => get<Member>(`/api/objects/${h}/members/${enc(name)}`),
  write: (h: number, name: string, value: unknown) => request<Member>('PUT', `/api/objects/${h}/members/${enc(name)}`, { body: { value } }),
  items: (h: number, offset: number, limit = 100) => get<ItemPage>(`/api/objects/${h}/items`, { offset, limit }),
  methods: (h: number) => get<Method[]>(`/api/objects/${h}/methods`),
  invoke: (h: number, method: string | number, args: unknown[]) => post<Value>(`/api/objects/${h}/methods/${enc(String(method))}`, { args }),
  image: (h: number, max: number) => request<Blob>('GET', `/api/objects/${h}/image.png`, { query: { max }, as: 'blob' }),
  clearHandles: () => request<unknown>('DELETE', '/api/handles'),

  assemblies: () => get<Assembly[]>('/api/assemblies'),
  assemblyTypes: (name: string) => get<{ namespace: string; types: TypeRow[] }[]>(`/api/assemblies/${enc(name)}/types`),
  searchTypes: (q: string, limit = 60) => get<TypeRow[]>('/api/types', { q, limit }),
  type: (name: string) => get<{ handle: number; type: TypeInfo }>(`/api/types/${enc(name)}`),
  instances: (name: string) => get<{ total: number; items: Value[] }>(`/api/types/${enc(name)}/instances`, { limit: 1000 }),

  eval: (code: string, mode: 'script' | 'program') => post<EvalResult>('/api/eval', { code, mode }),
  complete: (code: string, position: number, signal?: AbortSignal) => request<Completion>('POST', '/api/eval/complete', { body: { code, position }, signal }),
  vars: () => get<Variable[]>('/api/eval/vars'),
  resetSession: () => request<unknown>('DELETE', '/api/eval/session'),
  watch: (expressions: string[]) => post<WatchResult[]>('/api/watch', { expressions }),

  logs: (after: number, wait: number, signal?: AbortSignal) => get<Page<LogEntry>>('/api/logs', { after, wait, limit: 2000 }, signal),
  logFile: () => request<string>('GET', '/api/logs/file', { as: 'text' }),

  candidates: (ref: string) => get<MethodCandidate[]>('/api/methods', { q: ref }),
  hooks: () => get<Hook[]>('/api/hooks'),
  hook: (id: number) => get<Hook>(`/api/hooks/${id}`),
  createHook: (body: { method: string; action: HookAction; returnValue?: string; acceptFolded?: boolean }) => post<Hook>('/api/hooks', body),
  updateHook: (id: number, body: Partial<{ enabled: boolean; action: HookAction; returnValue: string; source: string }>) =>
    request<Hook>('PATCH', `/api/hooks/${id}`, { body }),
  deleteHook: (id: number) => request<unknown>('DELETE', `/api/hooks/${id}`),
  calls: (id: number, after: number, wait: number, signal?: AbortSignal) => get<Page<Call>>(`/api/hooks/${id}/calls`, { after, wait, limit: 200 }, signal),
  clearCalls: (id: number) => request<unknown>('DELETE', `/api/hooks/${id}/calls`),
};

/** User-facing text for an error; the API's own message unless a friendlier one exists. */
export function describe(e: unknown): string {
  if (!(e instanceof ApiError)) return e instanceof Error ? e.message : String(e);
  switch (e.code) {
    case 'main_thread_timeout': return '游戏主线程没有响应：可能正在加载，或游戏在后台被暂停（可在游戏设置中开启“后台运行”）';
    case 'destroyed': return '对象已被销毁（可能切换了场景）';
    case 'offline': return '无法连接到游戏';
    default: return e.message;
  }
}
