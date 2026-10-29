import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { api } from '../../api/client';
import type { Node, Scene } from '../../api/types';
import { copyText, debounce } from '../../lib/format';
import { insertIntoConsole } from '../../state/editor';
import { current, inspect } from '../../state/nav';
import { pin } from '../../state/session';
import { onBoot } from '../../state/status';
import { fail, openMenu, toast } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { Empty, IconButton, SearchBox, Spinner } from '../../ui/controls';

type Row =
  | { kind: 'scene'; key: string; scene: Scene; open: boolean }
  | { kind: 'node'; key: string; node: Node; depth: number; open: boolean; parent?: string };

let reloadScenes: () => void = () => {};
onBoot(() => reloadScenes());

export function SceneTree() {
  const [scenes, setScenes] = useState<Scene[] | null>(null);
  const [children, setChildren] = useState(new Map<number, Node[]>());
  const [open, setOpen] = useState(new Set<string>());
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Node[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const tree = useRef<HTMLDivElement>(null);

  const load = async () => {
    try {
      const next = await api.scenes();
      setScenes(next);
      // keep open nodes open: reload their children
      const fresh = new Map<number, Node[]>();
      const handles = [...open].filter(k => k.startsWith('n')).map(k => Number(k.slice(1)));
      await Promise.all(handles.map(h => api.children(h).then(c => fresh.set(h, c), () => {})));
      setChildren(fresh);
    } catch (e) {
      fail(e);
    }
  };
  reloadScenes = load;

  useEffect(() => {
    // scenes open by default, except DontDestroyOnLoad's internals
    setOpen(new Set(['s0', 's1', 's2', 's3']));
    load();
  }, []);

  const search = useMemo(() => debounce(async (q: string) => {
    if (!q.trim()) return setResults(null);
    try {
      setResults(await api.search(q.trim()));
    } catch (e) {
      fail(e);
    }
  }, 250), []);

  const toggle = async (key: string, node?: Node) => {
    const next = new Set(open);
    if (next.has(key)) next.delete(key);
    else {
      next.add(key);
      if (node && !children.has(node.handle)) {
        try {
          const list = await api.children(node.handle);
          setChildren(c => new Map(c).set(node.handle, list));
        } catch (e) {
          fail(e);
        }
      }
    }
    setOpen(next);
  };

  const rows: Row[] = useMemo(() => {
    if (!scenes) return [];
    const list: Row[] = [];
    const walk = (nodes: Node[], depth: number, parent: string) => {
      for (const node of nodes) {
        const key = 'n' + node.handle;
        const isOpen = open.has(key) && children.has(node.handle);
        list.push({ kind: 'node', key, node, depth, open: isOpen, parent });
        if (isOpen) walk(children.get(node.handle)!, depth + 1, key);
      }
    };
    scenes.forEach((scene, i) => {
      const key = 's' + i;
      list.push({ kind: 'scene', key, scene, open: open.has(key) });
      if (open.has(key)) walk(scene.roots, 1, key);
    });
    return list;
  }, [scenes, children, open]);

  const activate = (row: Row) => {
    setSelected(row.key);
    if (row.kind === 'node') inspect(row.node.handle, row.node.name);
    else toggle(row.key);
  };

  const keys = (e: KeyboardEvent) => {
    const i = rows.findIndex(r => r.key === selected);
    const row = rows[i];
    const select = (r?: Row) => r && (setSelected(r.key), tree.current?.querySelector(`[data-key="${r.key}"]`)?.scrollIntoView({ block: 'nearest' }));
    if (e.key === 'ArrowDown') select(rows[Math.min(rows.length - 1, i + 1)]);
    else if (e.key === 'ArrowUp') select(rows[Math.max(0, i - 1)]);
    else if (e.key === 'ArrowRight' && row) {
      if (!row.open && (row.kind === 'scene' || row.node.children > 0)) toggle(row.key, row.kind === 'node' ? row.node : undefined);
      else select(rows[i + 1]);
    } else if (e.key === 'ArrowLeft' && row) {
      if (row.open) toggle(row.key);
      else if (row.kind === 'node') select(rows.find(r => r.key === row.parent));
    } else if (e.key === 'Enter' && row) activate(row);
    else return;
    e.preventDefault();
  };

  const nodeMenu = (e: MouseEvent, node: Node) => openMenu(e, [
    { label: '检查', icon: 'right', action: () => inspect(node.handle, node.name) },
    {
      label: node.active ? '停用' : '激活', icon: 'eye', action: async () => {
        try {
          await api.invoke(node.handle, 'SetActive', [!node.active]);
          toast(`${node.name} 已${node.active ? '停用' : '激活'}`, 'ok');
          load();
        } catch (err) {
          fail(err);
        }
      },
    },
    { label: '收藏', icon: 'pin', action: () => pin({ kind: 'object', type: 'GameObject', text: node.name, handle: node.handle }, node.name) },
    { label: '写入控制台', icon: 'dollar', hint: '$' + node.handle, action: () => insertIntoConsole('$' + node.handle) },
    'separator',
    { label: '复制名称', icon: 'copy', action: () => copyText(node.name) },
  ]);

  const currentHandle = current.value?.handle;

  return (
    <>
      <div class="side-toolbar">
        <SearchBox value={query} placeholder="按名称查找 GameObject" onInput={q => { setQuery(q); search(q); }} />
        <IconButton icon="refresh" title="刷新场景" onClick={load} />
      </div>
      <div class="tree scroll" ref={tree} tabIndex={0} onKeyDown={keys}>
        {results ? (
          results.length === 0 ? <Empty>没有名称包含“{query}”的 GameObject</Empty>
            : results.map(node => (
              <div key={node.handle} class={`t-row${node.activeInHierarchy ? '' : ' inactive'}${node.handle === currentHandle ? ' selected' : ''}`}
                onClick={() => inspect(node.handle, node.name)} onContextMenu={e => nodeMenu(e, node)} style={{ paddingLeft: '10px' }}>
                <Icon name="cube" class="i ico" />
                <span class="label">{node.name}</span>
                {node.path && <span class="path">{node.path}</span>}
              </div>
            ))
        ) : !scenes ? <Empty><Spinner /></Empty> : rows.map(row => row.kind === 'scene' ? (
          <div key={row.key} data-key={row.key} class={`t-row scene${row.open ? ' open' : ''}${selected === row.key ? ' selected' : ''}`} onClick={() => activate(row)}>
            <span class="caret"><Icon name="chev" /></span>
            <Icon name="scene" class="i ico" />
            <span class="label">{row.scene.name}</span>
            <span class="meta">{row.scene.roots.length}</span>
          </div>
        ) : (
          <div key={row.key} data-key={row.key}
            class={`t-row${row.open ? ' open' : ''}${row.node.activeInHierarchy ? '' : ' inactive'}${selected === row.key || row.node.handle === currentHandle ? ' selected' : ''}`}
            style={{ paddingLeft: `${row.depth * 14}px` }} onClick={() => activate(row)} onContextMenu={e => nodeMenu(e, row.node)}>
            <span class={`caret${row.node.children > 0 ? '' : ' empty'}`} onClick={e => { e.stopPropagation(); toggle(row.key, row.node); }}><Icon name="chev" /></span>
            <Icon name="cube" class="i ico" />
            <span class="label">{row.node.name}</span>
            {row.node.components && <span class="meta">{row.node.components.map(c => <span key={c} class="tag">{c}</span>)}</span>}
          </div>
        ))}
      </div>
    </>
  );
}
