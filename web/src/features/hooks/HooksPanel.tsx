import { computed, signal } from '@preact/signals';
import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import { ApiError, api, token } from '../../api/client';
import type { Call, Hook } from '../../api/types';
import { copyText, fmtDuration, fmtTime } from '../../lib/format';
import { onBoot } from '../../state/status';
import { drawerTab, fail, toast } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { Button, Empty, IconButton, Switch } from '../../ui/controls';
import { ValueRow, ValueTree } from '../../ui/ValueTree';
import { openHookDialog, openSourceEditor } from './HookDialog';

export const hooks = signal<Hook[]>([]);
export const selectedHook = signal<number | null>(null);
export const hookCount = computed(() => hooks.value.length);

const actionLabel: Record<Hook['action'], string> = { log: '记录', skip: '跳过原方法', return: '返回固定值', custom: '自定义' };

export async function refreshHooks() {
  try {
    hooks.value = await api.hooks();
    if (selectedHook.value != null && !hooks.value.some(h => h.id === selectedHook.value)) selectedHook.value = null;
    if (selectedHook.value == null && hooks.value.length) selectedHook.value = hooks.value[hooks.value.length - 1].id;
  } catch (e) {
    if (!(e instanceof ApiError && e.status === 401)) fail(e);
  }
}

onBoot(() => refreshHooks());

export function HooksPanel() {
  useEffect(() => {
    if (!token.value) return;
    const timer = setInterval(() => drawerTab.value === 'hooks' && !document.hidden && refreshHooks(), 2000);
    return () => clearInterval(timer);
  }, [token.value]);

  const selected = hooks.value.find(h => h.id === selectedHook.value);
  return (
    <div class="hooks">
      <div class="hook-list scroll">
        <div class="hook-list-head">
          <span>Hook</span>
          <Button kind="ghost small" icon="plus" onClick={() => openHookDialog()}>新建</Button>
        </div>
        {hooks.value.length === 0 && (
          <Empty>无</Empty>
        )}
        {hooks.value.map(h => <HookItem key={h.id} hook={h} selected={h.id === selectedHook.value} />)}
      </div>
      <div class="hook-calls">{selected ? <CallStream key={selected.id} hook={selected} /> : <Empty>选择一个 Hook 查看调用记录</Empty>}</div>
    </div>
  );
}

function HookItem({ hook: h, selected }: { hook: Hook; selected: boolean }) {
  const update = async (body: Parameters<typeof api.updateHook>[1]) => {
    try {
      await api.updateHook(h.id, body);
      refreshHooks();
    } catch (e) {
      fail(e);
    }
  };
  const remove = async () => {
    try {
      await api.deleteHook(h.id);
      toast(`已移除 Hook #${h.id}`, 'ok');
      refreshHooks();
    } catch (e) {
      fail(e);
    }
  };
  const folded = h.native?.folded?.length ?? 0;

  return (
    <div class={`hook-item${selected ? ' selected' : ''}${h.enabled ? '' : ' off'}`} onClick={() => (selectedHook.value = h.id)}>
      <div class="hook-title">
        <span class="hook-id">#{h.id}</span>
        <span class="hook-type" title={h.method}>{h.declaringType}</span>
        <Switch checked={h.enabled} onChange={enabled => update({ enabled })} title={h.enabled ? '停用' : '启用'} />
      </div>
      <div class="hook-sig mono" title={h.method}>{h.signature}</div>
      <div class="hook-meta">
        <span class={`hook-action a-${h.action}`}>{actionLabel[h.action]}{h.action === 'return' && h.returnValue ? ` = ${h.returnValue}` : ''}</span>
        <span class="mono" title="命中次数 / 已记录">{h.hits.toLocaleString()} / {h.recorded.toLocaleString()}</span>
        {h.dropped > 0 && <span class="mono warn" title="超过每秒 200 条的调用只计数不记录">限流 {h.dropped}</span>}
        {folded > 0 && <span class="mono warn" title={`ICF：与 ${folded} 个方法共用同一份机器码；来自其他类的调用（${h.foreign}）已放行`}><Icon name="warning" />ICF {folded}</span>}
        <span class="spacer" />
        <IconButton small icon="code" title="编辑补丁源码" onClick={() => openSourceEditor(h.id)} />
        <IconButton small icon="trash" title="移除 Hook" onClick={remove} />
      </div>
      {h.native?.location && <div class="hook-native mono" title="点击复制地址" onClick={e => { e.stopPropagation(); copyText(h.native!.location!); }}>{h.native.location}</div>}
      {h.error && <div class="hook-error">{h.error}</div>}
    </div>
  );
}

const MaxCalls = 300;

function CallStream({ hook }: { hook: Hook }) {
  const [calls, setCalls] = useState<Call[]>([]);
  const [paused, setPaused] = useState(false);
  const [follow, setFollow] = useState(true);
  const list = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (paused) return;
    const abort = new AbortController();
    let cursor = -1;
    (async () => {
      while (!abort.signal.aborted) {
        try {
          const page = await api.calls(hook.id, cursor, cursor < 0 ? 0 : 20, abort.signal);
          cursor = page.cursor;
          const fresh = page.calls ?? [];
          if (fresh.length) setCalls(c => [...c, ...fresh].slice(-MaxCalls));
          if (drawerTab.value !== 'hooks') await new Promise(r => setTimeout(r, 1000));
        } catch (e) {
          if (abort.signal.aborted) return;
          await new Promise(r => setTimeout(r, 2000));
        }
      }
    })();
    return () => abort.abort();
  }, [hook.id, paused]);

  useLayoutEffect(() => {
    if (follow && list.current) list.current.scrollTop = list.current.scrollHeight;
  }, [calls, follow]);

  const clear = async () => {
    try {
      await api.clearCalls(hook.id);
      setCalls([]);
    } catch (e) {
      fail(e);
    }
  };

  return (
    <>
      <div class="log-toolbar">
        <span class="hook-calls-title mono" title={hook.method}>{hook.declaringType}::{hook.signature}</span>
        <span class="spacer" />
        <span class="muted">{calls.length} 条</span>
        <Button kind="ghost" icon={paused ? 'play' : 'pause'} onClick={() => setPaused(!paused)}>{paused ? '继续' : '暂停'}</Button>
        <Button kind="ghost" icon="trash" onClick={clear}>清空</Button>
      </div>
      <div class="call-list scroll" ref={list}
        onScroll={e => { const el = e.currentTarget; setFollow(el.scrollHeight - el.scrollTop - el.clientHeight < 40); }}>
        {calls.length === 0 && <div class="log-sep">{hook.enabled ? '等待调用…' : '这个 Hook 已停用'}</div>}
        <ValueTree compact>
          {calls.map(c => (
            <div key={c.seq} class={`call${c.exception ? ' failed' : ''}`}>
              <div class="call-head mono">
                <span class="tm">{fmtTime(c.time)}</span>
                <span title="帧号">f{c.frame}</span>
                <span title="耗时">{fmtDuration(c.durationUs)}</span>
                {!c.mainThread && <span class="warn" title="不在主线程上调用：对象只记录了类型">线程 {c.thread}</span>}
              </div>
              {c.instance && <ValueRow name="this" value={c.instance} path={`${c.seq}:this`} />}
              {c.args.map((a, i) => <ValueRow key={i} name={a.name} value={a.value} path={`${c.seq}:${i}`} />)}
              {c.result && <ValueRow name="return" value={c.result} path={`${c.seq}:r`} />}
              {c.exception && <div class="call-error">⚠ {c.exception}</div>}
            </div>
          ))}
        </ValueTree>
      </div>
    </>
  );
}
