import { useEffect, useMemo, useState } from 'preact/hooks';
import { api } from '../../api/client';
import type { Assembly, TypeRow } from '../../api/types';
import { copyText, debounce } from '../../lib/format';
import { insertIntoConsole } from '../../state/editor';
import { inspect } from '../../state/nav';
import { fail, openMenu } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { Empty, SearchBox, Spinner } from '../../ui/controls';

const groups: [Assembly['group'], string][] = [['game', '游戏'], ['unity', 'Unity'], ['il2cpp', 'IL2CPP / BCL 互操作'], ['mods', 'Mod 与加载器'], ['bcl', '.NET']];
const kindLetter: Record<string, string> = { class: 'C', struct: 'S', enum: 'E', interface: 'I', static: 'St', delegate: 'D' };

export async function openType(fullName: string, name = fullName) {
  try {
    const t = await api.type(fullName);
    inspect(t.handle, name);
  } catch (e) {
    fail(e);
  }
}

export function typeMenu(e: MouseEvent, t: TypeRow) {
  openMenu(e, [
    { label: '查看静态成员', icon: 'right', action: () => openType(t.fullName, t.name) },
    { label: '写入控制台', icon: 'dollar', action: () => insertIntoConsole(t.fullName.replace(/\+/g, '.').replace(/`\d+/g, '')) },
    'separator',
    { label: '复制完整类型名', icon: 'copy', action: () => copyText(t.fullName) },
  ]);
}

export const KindBadge = ({ kind }: { kind: string }) => <span class={`kind k-${kind}`}>{kindLetter[kind] ?? kind[0]?.toUpperCase()}</span>;

export function TypeBrowser() {
  const [assemblies, setAssemblies] = useState<Assembly[] | null>(null);
  const [types, setTypes] = useState(new Map<string, { namespace: string; types: TypeRow[] }[]>());
  const [open, setOpen] = useState(new Set<string>(['g:game']));
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<TypeRow[] | null>(null);

  useEffect(() => { api.assemblies().then(setAssemblies, fail); }, []);

  const search = useMemo(() => debounce(async (q: string) => {
    if (!q.trim()) return setResults(null);
    try {
      setResults(await api.searchTypes(q.trim(), 100));
    } catch (e) {
      fail(e);
    }
  }, 200), []);

  const toggle = async (key: string, assembly?: string) => {
    const next = new Set(open);
    if (next.has(key)) next.delete(key);
    else {
      next.add(key);
      if (assembly && !types.has(assembly)) {
        try {
          const list = await api.assemblyTypes(assembly);
          setTypes(t => new Map(t).set(assembly, list));
        } catch (e) {
          fail(e);
        }
      }
    }
    setOpen(next);
  };

  const typeRow = (t: TypeRow, indent: number, showNamespace = false) => (
    <div key={t.fullName} class="t-row" style={{ paddingLeft: `${indent}px` }} title={`${t.fullName} · ${t.assembly}`}
      onClick={() => openType(t.fullName, t.name)} onContextMenu={e => typeMenu(e, t)}>
      <KindBadge kind={t.kind} />
      <span class="label">{t.name}</span>
      {showNamespace && <span class="path">{t.namespace || '(全局)'}</span>}
      {t.unity && <span class="meta"><span class="tag">Unity</span></span>}
    </div>
  );

  return (
    <>
      <div class="side-toolbar">
        <SearchBox value={query} placeholder="搜索类型（支持首字母缩写）" onInput={q => { setQuery(q); search(q); }} />
      </div>
      <div class="tree scroll" tabIndex={0}>
        {results ? (results.length === 0 ? <Empty>没有匹配“{query}”的类型</Empty> : results.map(t => typeRow(t, 10, true)))
          : !assemblies ? <Empty><Spinner /></Empty>
            : groups.map(([group, title]) => {
              const list = assemblies.filter(a => a.group === group);
              if (!list.length) return null;
              const groupKey = 'g:' + group;
              return (
                <div key={group}>
                  <div class={`t-row group-head${open.has(groupKey) ? ' open' : ''}`} onClick={() => toggle(groupKey)}>
                    <span class="caret"><Icon name="chev" /></span>{title}<span class="meta">{list.length}</span>
                  </div>
                  {open.has(groupKey) && list.map(a => {
                    const key = 'a:' + a.name;
                    const namespaces = types.get(a.name);
                    return (
                      <div key={a.name}>
                        <div class={`t-row${open.has(key) ? ' open' : ''}`} style={{ paddingLeft: '14px' }} onClick={() => toggle(key, a.name)}>
                          <span class="caret"><Icon name="chev" /></span>
                          <Icon name="package" class="i ico" />
                          <span class="label">{a.name}</span>
                          <span class="meta">{a.types}</span>
                        </div>
                        {open.has(key) && (namespaces ? namespaces.map(ns => {
                          const nsKey = `n:${a.name}:${ns.namespace}`;
                          return (
                            <div key={ns.namespace}>
                              <div class={`t-row${open.has(nsKey) ? ' open' : ''}`} style={{ paddingLeft: '28px' }} onClick={() => toggle(nsKey)}>
                                <span class="caret"><Icon name="chev" /></span>
                                <span class="kind k-ns">{'{}'}</span>
                                <span class="label">{ns.namespace || '(全局)'}</span>
                                <span class="meta">{ns.types.length}</span>
                              </div>
                              {open.has(nsKey) && ns.types.map(t => typeRow(t, 50))}
                            </div>
                          );
                        }) : <div class="t-more"><Spinner /></div>)}
                      </div>
                    );
                  })}
                </div>
              );
            })}
      </div>
    </>
  );
}
