// Shapes of the debugger's JSON API (see /help/api). Optional fields are omitted when null.

export type ValueKind =
  | 'null' | 'string' | 'number' | 'bool' | 'char' | 'enum' | 'pointer'
  | 'type' | 'object' | 'struct' | 'destroyed' | 'error' | 'void';

export interface Value {
  kind: ValueKind;
  type?: string;
  text?: string;
  handle?: number;
  count?: number;
  length?: number;
  truncated?: boolean;
  color?: [number, number, number, number];
}

export type Editor = 'bool' | 'number' | 'string' | 'char' | 'enum' | 'flags' | 'color' | 'vector';

export interface Member {
  name: string;
  kind: 'field' | 'property' | 'constant';
  native?: boolean;
  type: string;
  declaringType: string;
  depth: number;
  static?: boolean;
  writable?: boolean;
  editor?: Editor;
  options?: string[];
  value?: Value;
  lazy?: 'static' | 'sideEffect';
  error?: string;
}

export interface TypeInfo {
  name: string;
  fullName: string;
  assembly: string;
  kind: string;
  native?: boolean;
  csharp?: string;
  bases: string[];
}

export type Facet = 'components' | 'items' | 'image' | 'instances';

export interface GameObjectInfo {
  handle: number;
  name: string;
  path: string;
  active: boolean;
  activeInHierarchy: boolean;
  layer: string;
  tag?: string;
  scene: string;
  ancestors: Value[];
}

export interface Inspection {
  handle: number;
  static: boolean;
  type: TypeInfo;
  value: Value;
  address?: string;
  count?: number;
  facets: Facet[];
  gameObject?: GameObjectInfo;
  members: Member[];
  text?: string;
}

export interface Node {
  handle: number;
  name: string;
  active: boolean;
  activeInHierarchy: boolean;
  children: number;
  components?: string[];
  path?: string;
}

export interface Scene {
  name: string;
  buildIndex: number;
  loaded: boolean;
  persistent?: boolean;
  roots: Node[];
}

export interface ComponentEntry {
  value: Value;
  enabled?: boolean;
  missing?: boolean;
}

export interface Item {
  index: number;
  key?: Value;
  value: Value;
}

export interface ItemPage {
  count?: number;
  offset: number;
  items: Item[];
  more: boolean;
}

export interface Parameter {
  name: string;
  type: string;
  editor?: Editor;
  options?: string[];
  optional?: boolean;
  default?: string;
}

export interface Method {
  id: number;
  name: string;
  signature: string;
  returnType: string;
  parameters: Parameter[];
  static?: boolean;
  declaringType: string;
  depth: number;
  invocable: boolean;
  ref: string;
}

export interface Assembly {
  name: string;
  types: number;
  native?: boolean;
  group: 'game' | 'unity' | 'il2cpp' | 'mods' | 'bcl';
}

export interface TypeRow {
  name: string;
  fullName: string;
  namespace: string;
  assembly: string;
  kind: string;
  unity?: boolean;
  native?: boolean;
}

export interface Diagnostic {
  id: string;
  severity: 'error' | 'warning';
  message: string;
  line: number;
  column: number;
  endLine: number;
  endColumn: number;
}

export interface EvalResult {
  ok: boolean;
  value?: Value;
  output?: string;
  error?: { code: 'compile_error' | 'runtime_error'; message: string; diagnostics?: Diagnostic[]; detail?: string };
  durationMs: number;
}

export interface Completion {
  from: number;
  to: number;
  items: { label: string; kind: string; detail?: string }[];
}

export interface Variable {
  name: string;
  type: string;
  value: Value;
}

export interface WatchResult {
  value?: Value;
  error?: string;
}

export interface GameStatus {
  product: string;
  version: string;
  unity: string;
  platform: string;
  fps: number;
  frame: number;
  time: number;
  timeScale: number;
  focused: boolean;
  runInBackground: boolean;
  activeScene: string;
  scenes: string[];
}

export interface Status {
  game?: GameStatus;
  mainThread: boolean;
  debugger: {
    version: string;
    boot: string;
    runtime: string;
    loader: { name: string; version: string };
    plugins: { id: string; name: string; version: string }[];
    handles: number;
    hooks: number;
    managedMb: number;
    workingSetMb: number;
  };
}

export type LogLevel = 'debug' | 'info' | 'message' | 'warn' | 'error' | 'fatal';

export interface LogEntry {
  seq: number;
  time: number;
  level: LogLevel;
  source: string;
  text: string;
}

export interface Page<T> {
  cursor: number;
  dropped: boolean;
  entries?: T[];
  calls?: T[];
}

export type HookAction = 'log' | 'skip' | 'return' | 'custom';

export interface NativeInfo {
  address: string;
  location?: string;
  folded?: string[];
}

export interface Hook {
  id: number;
  method: string;
  declaringType: string;
  signature: string;
  action: HookAction;
  returnValue?: string;
  enabled: boolean;
  hits: number;
  recorded: number;
  dropped: number;
  foreign: number;
  native?: NativeInfo;
  error?: string;
  createdAt: number;
  source?: string;
}

export interface Call {
  seq: number;
  time: number;
  frame: number;
  thread: number;
  mainThread: boolean;
  instance?: Value;
  args: { name: string; value: Value }[];
  result?: Value;
  exception?: string;
  durationUs: number;
}

export interface MethodCandidate {
  ref: string;
  signature: string;
  declaringType: string;
  static?: boolean;
  hookable: boolean;
  reason?: string;
  native?: NativeInfo;
}
