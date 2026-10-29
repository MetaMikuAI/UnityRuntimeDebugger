import { useEffect, useState } from 'preact/hooks';
import { api } from '../../api/client';
import type { Variable, WatchResult } from '../../api/types';
import { inspect } from '../../state/nav';
import { pins, unpin, watches } from '../../state/session';
import { fail, sideTab } from '../../state/ui';
import { Check, IconButton } from '../../ui/controls';
import { Value } from '../../ui/Value';
import { ValueRow, ValueTree } from '../../ui/ValueTree';

export function SessionPanel() {
  return (
    <div class="scroll session-scroll">
      <Pins />
      <Watches />
      <Variables />
    </div>
  );
}

function Pins() {
  return (
    <div class="section">
      <div class="section-head"><span>收藏</span><span class="muted">{pins.value.length || ''}</span></div>
      <div class="list">
        {pins.value.length === 0 && <div class="empty">在检查器或右键菜单里收藏对象</div>}
        {pins.value.map(p => (
          <div key={p.handle} class="l-item">
            <div class="l-head" onClick={() => inspect(p.handle, p.label)}>
              <span class="name">{p.label}</span>
              <span class="val"><Value value={p.value} link={false} /></span>
              <span class="acts"><IconButton small icon="x" title="取消收藏" onClick={() => unpin(p.handle)} /></span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Watches() {
  const [results, setResults] = useState<WatchResult[]>([]);
  const [live, setLive] = useState(true);
  const [input, setInput] = useState('');
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (!watches.value.length) return setResults([]);
    let cancelled = false;
    const refresh = () => api.watch(watches.value).then(r => { if (!cancelled) { setResults(r); setTick(t => t + 1); } }, () => {});
    refresh();
    if (!live) return () => { cancelled = true; };
    const timer = setInterval(() => sideTab.value === 'session' && !document.hidden && refresh(), 1000);
    return () => { cancelled = true; clearInterval(timer); };
  }, [watches.value, live]);

  return (
    <div class="section">
      <div class="section-head">
        <span>监视表达式</span>
        <Check checked={live} onChange={setLive} label="实时" title="每秒刷新" />
      </div>
      <form class="watch-form" autocomplete="off" onSubmit={e => {
        e.preventDefault();
        if (input.trim() && !watches.value.includes(input.trim())) watches.value = [...watches.value, input.trim()];
        setInput('');
      }}>
        <input class="mono" placeholder="C# 表达式，如 Time.frameCount 或 $12.position" spellcheck={false} value={input} onInput={e => setInput((e.target as HTMLInputElement).value)} />
      </form>
      <ValueTree compact version={tick} class="list">
        {watches.value.map((expr, i) => (
          <div key={expr} class="l-item watch">
            {results[i]?.error
              ? <div class="l-head"><span class="name">{expr}</span><span class="val val k-error" title={results[i].error}>⚠ {results[i].error}</span></div>
              : <ValueRow name={expr} value={results[i]?.value} path={'w:' + expr} />}
            <span class="acts"><IconButton small icon="x" title="移除" onClick={() => (watches.value = watches.value.filter(w => w !== expr))} /></span>
          </div>
        ))}
      </ValueTree>
    </div>
  );
}

export const varsVersion = { bump: () => {} };

function Variables() {
  const [vars, setVars] = useState<Variable[]>([]);
  const refresh = () => api.vars().then(setVars, fail);
  useEffect(() => {
    varsVersion.bump = refresh;
    refresh();
  }, []);

  return (
    <div class="section">
      <div class="section-head"><span>控制台变量</span><button type="button" class="link-btn" onClick={refresh}>刷新</button></div>
      <ValueTree compact class="list">
        {vars.length === 0 && <div class="empty">控制台里声明的变量会出现在这里</div>}
        {vars.map(v => <ValueRow key={v.name} name={v.name} value={v.value} path={'v:' + v.name} />)}
      </ValueTree>
    </div>
  );
}
