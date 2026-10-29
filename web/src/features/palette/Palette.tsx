import { Fragment } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { api, signOut } from '../../api/client';
import type { Node, TypeRow } from '../../api/types';
import { debounce } from '../../lib/format';
import { focusConsole } from '../../state/editor';
import { inspect } from '../../state/nav';
import { drawerMax, fail, paletteOpen, showDrawer, sideTab, theme, toast } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { openHookDialog } from '../hooks/HookDialog';
import { showHelp } from '../shell/TopBar';
import { KindBadge, openType } from '../types/TypeBrowser';

interface Entry {
  group: string;
  label: string;
  detail?: string;
  icon?: string;
  kind?: string;
  run: () => void;
}

const commands: Entry[] = [
  { group: '命令', label: '浏览场景', icon: 'layers', run: () => (sideTab.value = 'scene') },
  { group: '命令', label: '聚焦控制台', icon: 'terminal', run: () => { showDrawer('console'); focusConsole(); } },
  { group: '命令', label: '打开日志', icon: 'file', run: () => showDrawer('logs') },
  { group: '命令', label: '打开 Hook 面板', icon: 'hook', run: () => showDrawer('hooks') },
  { group: '命令', label: '新建 Hook…', icon: 'plus', run: () => openHookDialog() },
  { group: '命令', label: '重置控制台会话', icon: 'rotate', run: () => api.resetSession().then(() => toast('会话已重置', 'ok'), fail) },
  { group: '命令', label: '清空对象句柄', icon: 'trash', run: () => api.clearHandles().then(() => toast('句柄已清空', 'ok'), fail) },
  { group: '命令', label: '切换明暗主题', icon: 'contrast', run: () => (theme.value = theme.value === 'dark' ? 'light' : 'dark') },
  { group: '命令', label: '最大化 / 还原底部面板', icon: 'maximize', detail: 'Ctrl+`', run: () => (drawerMax.value = !drawerMax.value) },
  { group: '命令', label: '使用说明', icon: 'help', run: showHelp },
  { group: '命令', label: '退出登录', icon: 'x', run: signOut },
];

export function Palette() {
  const [query, setQuery] = useState('');
  const [types, setTypes] = useState<TypeRow[]>([]);
  const [nodes, setNodes] = useState<Node[]>([]);
  const [active, setActive] = useState(0);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => input.current?.focus(), []);

  const search = useMemo(() => debounce(async (q: string) => {
    if (!q || q.startsWith('>')) {
      setTypes([]);
      setNodes([]);
      return;
    }
    const [t, n] = await Promise.all([api.searchTypes(q, 8).catch(() => []), api.search(q).catch(() => [])]);
    setTypes(t);
    setNodes(n.slice(0, 8));
  }, 150), []);

  const close = () => (paletteOpen.value = false);
  const text = query.replace(/^>\s*/, '').toLowerCase();
  const entries: Entry[] = [
    ...(query.startsWith('>') ? [] : nodes.map(n => ({ group: 'GameObject', label: n.name, detail: n.path, icon: 'cube', run: () => inspect(n.handle, n.name) }))),
    ...(query.startsWith('>') ? [] : types.map(t => ({ group: '类型', label: t.name, detail: t.namespace, kind: t.kind, run: () => openType(t.fullName, t.name) }))),
    ...commands.filter(c => !text || c.label.toLowerCase().includes(text)),
  ];

  const pick = (entry?: Entry) => {
    if (!entry) return;
    close();
    entry.run();
  };

  const keys = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') setActive(a => Math.min(entries.length - 1, a + 1));
    else if (e.key === 'ArrowUp') setActive(a => Math.max(0, a - 1));
    else if (e.key === 'Enter') pick(entries[active]);
    else if (e.key === 'Escape') close();
    else return;
    e.preventDefault();
  };

  let group = '';
  return (
    <div class="overlay palette-overlay" onPointerDown={e => e.target === e.currentTarget && close()}>
      <div class="palette" role="dialog" aria-label="命令面板">
        <label class="palette-input">
          <Icon name="search" />
          <input ref={input} type="text" value={query} placeholder="类型名、GameObject 名，或 > 命令" spellcheck={false} autocomplete="off"
            onInput={e => { const q = (e.target as HTMLInputElement).value; setQuery(q); setActive(0); search(q.trim()); }} onKeyDown={keys} />
          <kbd>Esc</kbd>
        </label>
        <div class="palette-list scroll">
          {entries.length === 0 && <div class="p-empty">没有结果</div>}
          {entries.map((entry, i) => {
            const head = entry.group !== group ? (group = entry.group) : null;
            return (
              <Fragment key={i}>
                {head && <div class="p-group">{head}</div>}
                <div class={`p-item${i === active ? ' active' : ''}`} onMouseEnter={() => setActive(i)} onClick={() => pick(entry)}>
                  {entry.kind ? <KindBadge kind={entry.kind} /> : <Icon name={entry.icon ?? 'right'} />}
                  <span class="pl">{entry.label}</span>
                  {entry.detail && <span class="pd">{entry.detail}</span>}
                </div>
              </Fragment>
            );
          })}
        </div>
      </div>
    </div>
  );
}
