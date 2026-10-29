import { useEffect } from 'preact/hooks';
import { signOut } from '../../api/client';
import { fmtMb } from '../../lib/format';
import { connection, status } from '../../state/status';
import { modal, paletteOpen, theme } from '../../state/ui';
import { Mark, Icon } from '../../ui/Icon';
import { IconButton } from '../../ui/controls';

const connectionText = { connecting: '连接中', ok: '已连接', busy: '主线程无响应', offline: '未连接' };
const connectionClass = { connecting: '', ok: 'ok', busy: 'warn', offline: 'bad' };

export function TopBar() {
  const s = status.value;
  const game = s?.game;
  useEffect(() => {
    document.title = game ? `${game.product} · Unity Runtime Debugger` : 'Unity Runtime Debugger';
  }, [game?.product]);

  return (
    <header class="topbar">
      <div class="brand">
        <Mark class="brand-mark" />
        <span class="brand-name">Unity Runtime Debugger</span>
        <span class="brand-game">{game ? `${game.product} ${game.version}` : ''}</span>
      </div>
      <button class="palette-trigger" type="button" onClick={() => (paletteOpen.value = true)}>
        <Icon name="search" />
        <span>搜索类型、对象、命令…</span>
        <kbd>Ctrl</kbd><kbd>K</kbd>
      </button>
      <div class="status">
        <span class={`chip ${connectionClass[connection.value]}`} id="st-conn" title={s ? `${s.debugger.loader.name} · ${s.debugger.runtime}` : '连接状态'}>
          <i class="dot" /><span>{connectionText[connection.value]}</span>
        </span>
        {game && <span class="chip mono" id="st-fps" title={`帧 ${game.frame} · timeScale ${game.timeScale}`}>{Math.round(game.fps)} fps</span>}
        {game && <span class="chip" id="st-scene" title="当前场景">{game.activeScene}</span>}
        {s && <span class="chip mono" id="st-mem" title="托管内存 / 进程内存">{fmtMb(s.debugger.managedMb)} / {fmtMb(s.debugger.workingSetMb)}</span>}
        {s && <span class="chip mono" id="st-handles" title="已登记的对象句柄">${s.debugger.handles}</span>}
      </div>
      <div class="top-actions">
        <IconButton icon="contrast" title="切换主题" onClick={() => (theme.value = theme.value === 'dark' ? 'light' : 'dark')} />
        <IconButton icon="help" title="使用说明" onClick={showHelp} />
      </div>
    </header>
  );
}

export function showHelp() {
  modal.value = {
    title: '快捷键',
    body: (
      <>
        <table><tbody>
          <tr><td><kbd>Ctrl</kbd> <kbd>K</kbd></td><td>命令面板</td></tr>
          <tr><td><kbd>Alt</kbd> <kbd>←</kbd> / <kbd>→</kbd></td><td>后退 / 前进</td></tr>
          <tr><td><kbd>Ctrl</kbd> <kbd>`</kbd></td><td>最大化底部面板</td></tr>
          <tr><td><kbd>Enter</kbd> / <kbd>Shift</kbd> <kbd>Enter</kbd></td><td>执行 / 换行</td></tr>
          <tr><td><kbd>Ctrl</kbd> <kbd>Space</kbd></td><td>补全</td></tr>
          <tr><td><kbd>Ctrl</kbd> <kbd>L</kbd></td><td>清空控制台</td></tr>
        </tbody></table>
        <p>HTTP API：<a href="/help" target="_blank">/help</a> · 命令行：<a href="/urdbg.py" download>urdbg.py</a></p>
        <p><button type="button" class="link-btn" onClick={() => { modal.value = null; signOut(); }}>退出登录</button></p>
      </>
    ),
  };
}
