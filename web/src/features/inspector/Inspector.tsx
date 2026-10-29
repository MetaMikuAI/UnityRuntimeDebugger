import { Fragment } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { api, describe } from '../../api/client';
import type { Inspection } from '../../api/types';
import { copyText } from '../../lib/format';
import { focusConsole, insertIntoConsole } from '../../state/editor';
import { current, go, history, inspect, position, retitle } from '../../state/nav';
import { pin } from '../../state/session';
import { fail, paletteOpen, showDrawer, sideTab, toast } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { IconButton, Spinner, Switch, Tabs } from '../../ui/controls';
import { openType } from '../types/TypeBrowser';
import { ComponentsTab, ImageTab, InstancesTab, ItemsTab, MembersTab, MethodsTab } from './tabs';

type Tab = 'components' | 'members' | 'items' | 'methods' | 'instances' | 'image';

export function Inspector() {
  const entry = current.value;
  return (
    <div class="inspector">
      <NavBar />
      <div class="inspector-body scroll">
        {entry ? <ObjectView key={entry.handle} handle={entry.handle} /> : <Welcome />}
      </div>
    </div>
  );
}

function NavBar() {
  const list = history.value;
  const at = position.value;
  const crumbs = list.slice(Math.max(0, at - 5), at + 1);
  return (
    <div class="nav-bar">
      <IconButton icon="left" title="后退 (Alt+←)" disabled={at <= 0} onClick={() => go(at - 1)} />
      <IconButton icon="right" title="前进 (Alt+→)" disabled={at >= list.length - 1} onClick={() => go(at + 1)} />
      <div class="crumbs">
        {crumbs.map((c, i) => {
          const index = at - crumbs.length + 1 + i;
          return (
            <Fragment key={index}>
              {i > 0 && <span class="crumb-sep"><Icon name="chev" /></span>}
              <button type="button" class={`crumb${index === at ? ' current' : ''}`} onClick={() => go(index)}>{c.title}</button>
            </Fragment>
          );
        })}
      </div>
    </div>
  );
}

function ObjectView({ handle }: { handle: number }) {
  const [data, setData] = useState<Inspection | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [live, setLive] = useState(false);
  const [statics, setStatics] = useState(false);
  const [version, setVersion] = useState(0);
  const [tab, setTab] = useState<Tab | null>(null);

  const load = async () => {
    try {
      const next = await api.inspect(handle, statics);
      setData(next);
      setError(null);
      retitle(handle, next.value.text || next.type.name);
    } catch (e) {
      setError(describe(e));
    }
  };

  useEffect(() => { load(); }, [handle, statics]);
  useEffect(() => {
    if (!live) return;
    const timer = setInterval(() => {
      if (document.hidden || document.querySelector('.v-edit')) return;
      load();
      setVersion(v => v + 1);
    }, 1000);
    return () => clearInterval(timer);
  }, [live, handle, statics]);

  if (error && !data) return <div class="pane-note err">{error}</div>;
  if (!data) return <div class="pane-note"><Spinner /></div>;

  const tabs: { id: Tab; label: string; icon: string; count?: number }[] = [];
  if (data.facets.includes('components')) tabs.push({ id: 'components', label: '组件', icon: 'box' });
  tabs.push({ id: 'members', label: '成员', icon: 'list', count: data.members.length });
  if (data.facets.includes('items')) tabs.push({ id: 'items', label: '元素', icon: 'braces', count: data.count });
  tabs.push({ id: 'methods', label: '方法', icon: 'method' });
  if (data.facets.includes('instances')) tabs.push({ id: 'instances', label: '实例', icon: 'cube' });
  if (data.facets.includes('image')) tabs.push({ id: 'image', label: '图像', icon: 'image' });
  const active = tab && tabs.some(t => t.id === tab) ? tab : tabs[0].id;

  return (
    <>
      <Header data={data} live={live} setLive={setLive} reload={() => { load(); setVersion(v => v + 1); }} stale={error} />
      {data.text != null ? <pre class="string-view">{data.text}</pre> : (
        <>
          <Tabs class="obj-tabs" value={active} onChange={setTab} tabs={tabs} />
          {active === 'components' && <ComponentsTab handle={handle} version={version} />}
          {active === 'members' && <MembersTab data={data} version={version} statics={statics} setStatics={setStatics} />}
          {active === 'items' && <ItemsTab handle={handle} version={version} />}
          {active === 'methods' && <MethodsTab data={data} />}
          {active === 'instances' && <InstancesTab type={data.type.fullName} />}
          {active === 'image' && <ImageTab handle={handle} name={data.value.text ?? data.type.name} />}
        </>
      )}
    </>
  );
}

function Header({ data, live, setLive, reload, stale }: { data: Inspection; live: boolean; setLive: (v: boolean) => void; reload: () => void; stale: string | null }) {
  const node = data.gameObject;
  const title = data.static ? data.type.name : data.value.text || data.type.name;
  const icon = data.static ? 'braces' : node ? 'cube' : data.facets.includes('items') ? 'list' : 'box';

  const setActive = async (active: boolean) => {
    try {
      await api.invoke(node!.handle, 'SetActive', [active]);
      reload();
    } catch (e) {
      fail(e);
    }
  };

  return (
    <div class="obj-head">
      <div class="obj-title-row">
        <span class="obj-icon"><Icon name={icon} /></span>
        <span class={`obj-title${data.static ? ' mono' : ''}`} title={title}>{title}</span>
        <span class={`obj-kind${data.static ? ' static' : ''}`}>{data.static ? 'static' : data.type.kind}</span>
        {data.value.kind === 'destroyed' && <span class="obj-kind bad">已销毁</span>}
        <div class="obj-actions">
          <IconButton icon="bolt" title={live ? '停止实时刷新' : '实时刷新（每秒）'} on={live} onClick={() => setLive(!live)} />
          <IconButton icon="refresh" title="刷新" onClick={reload} />
          <IconButton icon="pin" title="收藏" onClick={() => { pin(data.value, title); toast('已收藏', 'ok'); }} />
          <IconButton icon="dollar" title={`在控制台引用 $${data.handle}`} onClick={() => { insertIntoConsole('$' + data.handle); focusConsole(); }} />
        </div>
      </div>
      <div class="obj-sub">
        {!data.static && (
          <span class="type-link mono" title={`查看 ${data.type.fullName} 的静态成员`} onClick={() => openType(data.type.fullName, data.type.name)}>{data.type.fullName}</span>
        )}
        <span class="mono">{data.type.assembly}</span>
        <span class="handle" title="点击插入到控制台" onClick={() => insertIntoConsole('$' + data.handle)}>${data.handle}</span>
        {data.address && <span class="mono copyable" title="复制地址" onClick={() => copyText(data.address!)}>{data.address}</span>}
        {data.type.bases.length > 1 && <span class="mono" title="继承链">继承 {data.type.bases.slice(1).join(' › ')}</span>}
        {stale && <span class="err" title={stale}>⚠ 刷新失败</span>}
      </div>
      {node && (
        <div class="obj-node">
          <span class="node-path">
            {node.ancestors.map(a => <Fragment key={a.handle}><button type="button" onClick={() => a.handle && inspect(a.handle, a.text)}>{a.text}</button>/</Fragment>)}
            <button type="button" onClick={() => inspect(node.handle, node.name)}><b>{node.name}</b></button>
          </span>
          <Switch checked={node.active} onChange={setActive} label={node.activeInHierarchy ? '激活' : node.active ? '激活（父级未激活）' : '未激活'} />
          <span class="pill" title="Layer">{node.layer}</span>
          {node.tag && <span class="pill" title="Tag">{node.tag}</span>}
          <span class="pill" title="场景">{node.scene}</span>
        </div>
      )}
    </div>
  );
}

function Welcome() {
  const cards = [
    { icon: 'layers', title: '场景', text: 'GameObject 层级', action: () => (sideTab.value = 'scene') },
    { icon: 'braces', title: '类型', text: '程序集、静态成员与实例', action: () => (sideTab.value = 'types') },
    { icon: 'terminal', title: '控制台', text: 'C# 会话', action: () => { showDrawer('console'); focusConsole(); } },
    { icon: 'hook', title: 'Hook', text: '记录与改写方法调用', action: () => showDrawer('hooks') },
    { icon: 'search', title: '命令面板', text: 'Ctrl+K', action: () => (paletteOpen.value = true) },
    { icon: 'file', title: '日志', text: '加载器与游戏日志', action: () => showDrawer('logs') },
  ];
  return (
    <div class="welcome">
      <h2>Unity Runtime Debugger</h2>
      <div class="welcome-grid">
        {cards.map(c => (
          <button key={c.title} type="button" class="welcome-card" onClick={c.action}>
            <Icon name={c.icon} /><b>{c.title}</b><span>{c.text}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
