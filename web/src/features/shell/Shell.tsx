import { useEffect } from 'preact/hooks';
import { go, position } from '../../state/nav';
import { drawerMax, drawerTab, menu, pane, paletteOpen, sideTab, sizes, type DrawerTab, type SideTab } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { IconButton, Tabs } from '../../ui/controls';
import { Console } from '../console/Console';
import { HooksPanel, hookCount } from '../hooks/HooksPanel';
import { Inspector } from '../inspector/Inspector';
import { LogsPanel, unseenErrors } from '../logs/LogsPanel';
import { Palette } from '../palette/Palette';
import { SceneTree } from '../scene/SceneTree';
import { SessionPanel } from '../session/SessionPanel';
import { TypeBrowser } from '../types/TypeBrowser';
import { Resizer } from './Resizer';
import { TopBar } from './TopBar';

const sideTabs: { id: SideTab; label: string; icon: string }[] = [
  { id: 'scene', label: '场景', icon: 'layers' },
  { id: 'types', label: '类型', icon: 'braces' },
  { id: 'session', label: '会话', icon: 'bookmark' },
];

export function Shell() {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        paletteOpen.value = true;
      } else if (e.ctrlKey && e.key === '`') {
        e.preventDefault();
        drawerMax.value = !drawerMax.value;
      } else if (e.altKey && e.key === 'ArrowLeft') {
        e.preventDefault();
        go(position.value - 1);
      } else if (e.altKey && e.key === 'ArrowRight') {
        e.preventDefault();
        go(position.value + 1);
      } else if (e.key === 'Escape') {
        menu.value = null;
      }
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, []);

  const drawerTabs: { id: DrawerTab; label: string; icon: string; badge?: number; count?: number }[] = [
    { id: 'console', label: '控制台', icon: 'terminal' },
    { id: 'logs', label: '日志', icon: 'file', badge: drawerTab.value === 'logs' ? 0 : unseenErrors.value },
    { id: 'hooks', label: 'Hook', icon: 'hook', count: hookCount.value || undefined },
  ];

  return (
    <div class="app" data-pane={pane.value} style={{ '--side-w': sizes.value.side + 'px', '--drawer-h': sizes.value.drawer + 'px' }}>
      <TopBar />
      <main class="workspace">
        <aside class="sidebar">
          <Tabs class="side-tabs" value={sideTab.value} onChange={t => (sideTab.value = t)} tabs={sideTabs} />
          <section class="side-panel" hidden={sideTab.value !== 'scene'}><SceneTree /></section>
          <section class="side-panel" hidden={sideTab.value !== 'types'}><TypeBrowser /></section>
          <section class="side-panel" hidden={sideTab.value !== 'session'}><SessionPanel /></section>
        </aside>
        <Resizer axis="x" onDrag={x => (sizes.value = { ...sizes.value, side: Math.max(200, Math.min(innerWidth * 0.6, x)) })} />
        <section class="main">
          <Inspector />
          <Resizer axis="y" onDrag={(_, y) => (sizes.value = { ...sizes.value, drawer: Math.max(120, Math.min(innerHeight - 160, innerHeight - y)) })} />
          <div class={`drawer${drawerMax.value ? ' max' : ''}`}>
            <Tabs class="drawer-tabs" value={drawerTab.value} onChange={t => (drawerTab.value = t)} tabs={drawerTabs}>
              <span class="spacer" />
              <IconButton icon="maximize" title="最大化 / 还原 (Ctrl+`)" class="drawer-max" on={drawerMax.value} onClick={() => (drawerMax.value = !drawerMax.value)} />
            </Tabs>
            <section class="drawer-panel" hidden={drawerTab.value !== 'console'}><Console /></section>
            <section class="drawer-panel" hidden={drawerTab.value !== 'logs'}><LogsPanel /></section>
            <section class="drawer-panel" hidden={drawerTab.value !== 'hooks'}><HooksPanel /></section>
          </div>
        </section>
      </main>
      <MobileNav />
      {paletteOpen.value && <Palette />}
    </div>
  );
}

/** Phones: one pane at a time. */
function MobileNav() {
  const items = [
    { id: 'side', label: '浏览', icon: 'layers' },
    { id: 'main', label: '检查', icon: 'cube' },
    { id: 'drawer', label: '控制台', icon: 'terminal' },
  ] as const;
  return (
    <nav class="mobile-nav">
      {items.map(i => (
        <button key={i.id} type="button" class={pane.value === i.id ? 'active' : ''} onClick={() => (pane.value = i.id)}>
          <Icon name={i.icon} />{i.label}
        </button>
      ))}
    </nav>
  );
}
