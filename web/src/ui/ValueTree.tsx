import { createContext } from 'preact';
import { useContext, useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import { api } from '../api/client';
import type { Inspection, Item, ItemPage, Member, Value as V } from '../api/types';
import { canExpand, copyText, valueText } from '../lib/format';
import { insertIntoConsole } from '../state/editor';
import { inspect } from '../state/nav';
import { addWatch, pin } from '../state/session';
import { fail, openMenu, toast } from '../state/ui';
import { Icon } from './Icon';
import { IconButton, Spinner } from './controls';
import { Value } from './Value';

/**
 * Shared by every row of one tree: which paths are open (kept across refreshes) and a version
 * that bumps on live refresh so open children reload too.
 */
interface TreeState {
  open: Set<string>;
  version: number;
  compact: boolean;
}

const Tree = createContext<TreeState>({ open: new Set(), version: 0, compact: false });

export function ValueTree({ children, version = 0, compact = false, class: cls = '' }: {
  children: preact.ComponentChildren;
  version?: number;
  compact?: boolean;
  class?: string;
}) {
  const open = useRef(new Set<string>()).current;
  return (
    <Tree.Provider value={{ open, version, compact }}>
      <div class={`vtree${compact ? ' compact' : ''} ${cls}`}>{children}</div>
    </Tree.Provider>
  );
}

const signature = (m: Member) => m.error ? 'e:' + m.error : `${m.value?.kind}:${m.value?.text}:${m.value?.count}`;

/** Members of one object. With `grouped`, members are sectioned by declaring type. */
export function MemberList({ members, owner, depth = 0, path = '', grouped = false }: {
  members: Member[];
  owner: number;
  depth?: number;
  path?: string;
  grouped?: boolean;
}) {
  if (!grouped) return <>{members.map(m => <MemberRow key={m.name} member={m} owner={owner} depth={depth} path={`${path}/${m.name}`} />)}</>;

  const groups: [string, Member[]][] = [];
  for (const m of members) {
    const last = groups[groups.length - 1];
    if (last && last[0] === m.declaringType) last[1].push(m);
    else groups.push([m.declaringType, [m]]);
  }
  return <>{groups.map(([decl, list]) => <Group key={decl} title={decl} count={list.length} depth={depth}>
    {list.map(m => <MemberRow key={m.name} member={m} owner={owner} depth={depth} path={`${path}/${m.name}`} />)}
  </Group>)}</>;
}

function Group({ title, count, depth, children, startOpen = true }: { title: string; count: number; depth: number; children: preact.ComponentChildren; startOpen?: boolean }) {
  const [open, setOpen] = useState(startOpen);
  return (
    <>
      <div class={`v-group${open ? ' open' : ''}`} style={{ '--depth': depth }} onClick={() => setOpen(!open)}>
        <Icon name="chev" /><span>{title}</span><span class="ct">{count}</span><span class="ln" />
      </div>
      {open && children}
    </>
  );
}

function MemberRow({ member, owner, depth, path }: { member: Member; owner: number; depth: number; path: string }) {
  const tree = useContext(Tree);
  const [m, setM] = useState(member);
  const [editing, setEditing] = useState(false);
  const [, rerender] = useState(0);
  useEffect(() => { if (!editing) setM(member); }, [member]);

  const expandable = canExpand(m.value) && !m.error;
  const open = expandable && tree.open.has(path);
  const toggle = () => {
    if (!expandable) return;
    open ? tree.open.delete(path) : tree.open.add(path);
    rerender(x => x + 1);
  };

  const row = useFlash(signature(m));
  const marker = m.kind === 'property' ? 'mk prop' : m.kind === 'constant' ? 'mk const' : 'mk';
  const markerTitle = m.native ? '原生字段（Il2Cpp）' : m.kind === 'property' ? '属性' : m.kind === 'constant' ? '常量' : '托管字段';

  const readLazy = async () => {
    try {
      setM(await api.member(owner, m.name));
    } catch (e) {
      fail(e);
    }
  };

  const commit = async (text: string) => {
    setEditing(false);
    try {
      setM(await api.write(owner, m.name, text));
      toast(`${m.name} 已更新`, 'ok');
    } catch (e) {
      fail(e);
    }
  };

  const menuItems = (e: MouseEvent) => openMenu(e, [
    ...(m.value?.handle ? [
      { label: '检查', icon: 'right', action: () => inspect(m.value!.handle!, m.value!.text || m.name) },
      { label: '收藏', icon: 'pin', action: () => pin(m.value!, m.name) },
      { label: '写入控制台', icon: 'dollar', hint: '$' + m.value.handle, action: () => insertIntoConsole('$' + m.value!.handle) },
    ] : []),
    { label: '添加到监视', icon: 'eye', hint: `$${owner}.${m.name}`, action: () => addWatch(`$${owner}.${m.name}`) },
    'separator',
    { label: '复制值', icon: 'copy', action: () => copyText(valueText(m.value)) },
    { label: '复制名称', action: () => copyText(m.name) },
    { label: '复制类型名', action: () => copyText(m.type) },
  ]);

  return (
    <div class="v-node">
      <div ref={row} class={`v-row${open ? ' open' : ''}`} style={{ '--depth': depth }} onContextMenu={menuItems}
        onDblClick={e => !(e.target as HTMLElement).closest('input,select,button,.v-val') && toggle()}>
        <div class="v-name" onClick={e => !(e.target as HTMLElement).closest('.link') && toggle()}>
          <span class={`caret${expandable ? '' : ' empty'}`}><Icon name="chev" /></span>
          <span class={marker} style={m.kind === 'field' && !m.native ? { background: 'var(--fg-3)' } : undefined} title={markerTitle} />
          {m.static && <span class="st" title="static">S</span>}
          <span class={`nm${m.kind === 'property' ? ' prop' : m.kind === 'constant' ? ' const' : ''}`} title={`${m.name} · ${m.declaringType}`}>{m.name}</span>
        </div>
        {!tree.compact && <div class="v-type" title={m.type}>{m.type}</div>}
        <div class="v-val" onDblClick={e => { if (m.writable && m.editor && m.editor !== 'bool') { e.stopPropagation(); setEditing(true); } }}>
          {m.error ? <span class="val k-error" title={m.error}>⚠ {m.error}</span>
            : m.lazy ? (
              <button type="button" class="link-btn" onClick={readLazy}
                title={m.lazy === 'sideEffect' ? '这个 getter 会改变状态（创建材质 / 网格副本、推进随机数等），只在点击时读取' : '静态属性默认不读取：单例的 Instance 等 getter 可能会顺带创建对象'}>
                {m.lazy === 'sideEffect' ? '读取（有副作用）' : '读取'}
              </button>
            ) : editing ? <InlineEditor member={m} onCommit={commit} onCancel={() => setEditing(false)} />
              : <>
                {m.writable && m.editor === 'bool' && (
                  <input type="checkbox" class="bool-toggle" title="切换" checked={m.value?.text === 'true'} onChange={e => commit(String((e.target as HTMLInputElement).checked))} />
                )}
                <Value value={m.value} />
                <ValueActions value={m.value} edit={m.writable && m.editor && m.editor !== 'bool' ? () => setEditing(true) : undefined} />
              </>}
        </div>
      </div>
      {open && <Expansion value={m.value!} depth={depth + 1} path={path} />}
    </div>
  );
}

function ValueActions({ value, edit }: { value?: V; edit?: () => void }) {
  return (
    <div class="v-acts">
      {edit && <IconButton small icon="edit" title="编辑（双击值也可以）" class="edit-btn" onClick={edit} />}
      {value?.handle && <IconButton small icon="dollar" title={`在控制台引用 $${value.handle}`} onClick={() => insertIntoConsole('$' + value.handle)} />}
      {value?.handle && value.kind !== 'string' && <IconButton small icon="right" title="打开" onClick={() => inspect(value.handle!, value.text || value.type)} />}
      <IconButton small icon="copy" title="复制值" onClick={() => copyText(valueText(value))} />
    </div>
  );
}

/** Text of a member as its editor expects it. */
function editText(m: Member) {
  const v = m.value;
  if (!v || v.kind === 'null') return '';
  if (m.editor === 'color' && v.color) {
    const hex = v.color.map(c => Math.round(Math.max(0, Math.min(1, c)) * 255).toString(16).padStart(2, '0')).join('');
    return '#' + hex;
  }
  return v.text ?? '';
}

function InlineEditor({ member: m, onCommit, onCancel }: { member: Member; onCommit: (text: string) => void; onCancel: () => void }) {
  const [text, setText] = useState(editText(m));
  const input = useRef<HTMLInputElement & HTMLSelectElement>(null);
  useLayoutEffect(() => {
    input.current?.focus();
    input.current?.select?.();
  }, []);

  const keys = (e: KeyboardEvent) => {
    if (e.key === 'Enter') { e.preventDefault(); onCommit(text); }
    if (e.key === 'Escape') { e.preventDefault(); onCancel(); }
  };
  const blur = () => setTimeout(() => !input.current?.parentElement?.contains(document.activeElement) && onCancel(), 150);

  return (
    <div class="v-edit">
      {m.editor === 'enum' ? (
        <select ref={input} value={text} onChange={e => onCommit((e.target as HTMLSelectElement).value)} onKeyDown={keys} onBlur={blur}>
          {(m.options ?? []).map(o => <option key={o} value={o}>{o}</option>)}
        </select>
      ) : (
        <input ref={input} type="text" spellcheck={false} value={text} placeholder={m.editor === 'vector' ? m.options?.join(', ') : undefined}
          onInput={e => setText((e.target as HTMLInputElement).value)} onKeyDown={keys} onBlur={blur} />
      )}
      {m.editor === 'color' && (
        <input type="color" title="取色" value={text.startsWith('#') ? text.slice(0, 7) : '#ffffff'}
          onInput={e => setText((e.target as HTMLInputElement).value + (text.length === 9 ? text.slice(7) : 'ff'))} />
      )}
    </div>
  );
}

/** The inside of an opened value: its elements (collections) and members. */
export function Expansion({ value, depth, path }: { value: V; depth: number; path: string }) {
  const tree = useContext(Tree);
  const [data, setData] = useState<Inspection | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.inspect(value.handle!).then(d => !cancelled && setData(d), e => !cancelled && setError(e.message));
    return () => { cancelled = true; };
  }, [value.handle, tree.version]);

  if (error) return <div class="v-msg err" style={{ '--depth': depth }}>{error}</div>;
  if (!data) return <div class="loading-row" style={{ '--depth': depth }}><Spinner /></div>;
  if (data.text != null) return <div class="v-msg" style={{ '--depth': depth }}>{data.text}</div>;

  const items = data.facets.includes('items');
  return (
    <>
      {items && <ItemList handle={data.handle} depth={depth} path={path + '#'} />}
      {data.members.length === 0 && !items && <div class="v-msg" style={{ '--depth': depth }}>没有可显示的成员</div>}
      {items
        ? data.members.length > 0 && <Group title="成员" count={data.members.length} depth={depth} startOpen={false}><MemberList members={data.members} owner={data.handle} depth={depth} path={path} /></Group>
        : <MemberList members={data.members} owner={data.handle} depth={depth} path={path} />}
    </>
  );
}

const PageSize = 50;

/** Collection elements, a page at a time. */
export function ItemList({ handle, depth, path }: { handle: number; depth: number; path: string }) {
  const tree = useContext(Tree);
  const [page, setPage] = useState<ItemPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const shown = useRef(PageSize);

  const load = async (count: number) => {
    try {
      setPage(await api.items(handle, 0, count));
      shown.current = count;
    } catch (e) {
      setError((e as Error).message);
    }
  };

  useEffect(() => { load(shown.current); }, [handle, tree.version]);

  if (error) return <div class="v-msg err" style={{ '--depth': depth }}>{error}</div>;
  if (!page) return <div class="loading-row" style={{ '--depth': depth }}><Spinner /></div>;
  if (page.items.length === 0) return <div class="v-msg" style={{ '--depth': depth }}>空集合</div>;

  return (
    <>
      {page.items.map(item => <ItemRow key={item.index} item={item} depth={depth} path={`${path}${item.index}`} />)}
      {page.more && (
        <div class="v-more" style={{ '--depth': depth }}>
          <button type="button" class="link-btn" onClick={() => load(shown.current + PageSize * 4)}>
            加载更多{page.count != null && `（${page.items.length} / ${page.count}）`}
          </button>
        </div>
      )}
    </>
  );
}

function ItemRow({ item, depth, path }: { item: Item; depth: number; path: string }) {
  const tree = useContext(Tree);
  const [, rerender] = useState(0);
  const expandable = canExpand(item.value);
  const open = expandable && tree.open.has(path);
  const toggle = () => {
    if (!expandable) return;
    open ? tree.open.delete(path) : tree.open.add(path);
    rerender(x => x + 1);
  };
  const row = useFlash(`${item.value.kind}:${item.value.text}:${item.key?.text}`);

  return (
    <div class="v-node">
      <div ref={row} class={`v-row${open ? ' open' : ''}`} style={{ '--depth': depth }} onContextMenu={e => valueMenu(e, item.value, `[${item.index}]`)}>
        <div class="v-name" onClick={e => !(e.target as HTMLElement).closest('.link') && toggle()}>
          <span class={`caret${expandable ? '' : ' empty'}`}><Icon name="chev" /></span>
          {item.key ? <Value value={item.key} /> : <span class="nm idx">[{item.index}]</span>}
        </div>
        {!tree.compact && <div class="v-type">{item.value.type ?? ''}</div>}
        <div class="v-val">
          <Value value={item.value} />
          <ValueActions value={item.value} />
        </div>
      </div>
      {open && <Expansion value={item.value} depth={depth + 1} path={path} />}
    </div>
  );
}

/** A standalone value (console result, watch, hook argument): one row, expandable. */
export function ValueRow({ name, value, path, depth = 0 }: { name: preact.ComponentChildren; value?: V; path?: string; depth?: number }) {
  const tree = useContext(Tree);
  const key = path ?? (typeof name === 'string' ? name : 'value');
  const [, rerender] = useState(0);
  const expandable = canExpand(value);
  const open = expandable && tree.open.has(key);
  const toggle = () => {
    if (!expandable) return;
    open ? tree.open.delete(key) : tree.open.add(key);
    rerender(x => x + 1);
  };

  return (
    <div class="v-node">
      <div class={`v-row${open ? ' open' : ''}`} style={{ '--depth': depth }} onContextMenu={e => value && valueMenu(e, value, typeof name === 'string' ? name : 'value')}>
        <div class="v-name" onClick={e => !(e.target as HTMLElement).closest('.link') && toggle()}>
          <span class={`caret${expandable ? '' : ' empty'}`}><Icon name="chev" /></span>
          <span class="nm">{name}</span>
        </div>
        {!tree.compact && <div class="v-type">{value?.type ?? ''}</div>}
        <div class="v-val"><Value value={value} /><ValueActions value={value} /></div>
      </div>
      {open && value && <Expansion value={value} depth={depth + 1} path={key} />}
    </div>
  );
}

export function valueMenu(e: MouseEvent, value: V, label: string) {
  openMenu(e, [
    ...(value.handle ? [
      { label: '检查', icon: 'right', action: () => inspect(value.handle!, value.text || label) },
      { label: '收藏', icon: 'pin', action: () => pin(value, label) },
      { label: '写入控制台', icon: 'dollar', hint: '$' + value.handle, action: () => insertIntoConsole('$' + value.handle) },
      'separator' as const,
    ] : []),
    { label: '复制值', icon: 'copy', action: () => copyText(valueText(value)) },
    ...(value.type ? [{ label: '复制类型名', action: () => copyText(value.type!) }] : []),
  ]);
}

/** Flashes the row when its value changes between renders (live refresh). */
function useFlash(sig: string) {
  const ref = useRef<HTMLDivElement>(null);
  const previous = useRef(sig);
  useEffect(() => {
    if (previous.current !== sig && ref.current) {
      ref.current.classList.remove('flash');
      void ref.current.offsetWidth;
      ref.current.classList.add('flash');
    }
    previous.current = sig;
  }, [sig]);
  return ref;
}
