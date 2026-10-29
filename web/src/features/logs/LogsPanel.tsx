import { signal } from '@preact/signals';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import { api, token } from '../../api/client';
import type { LogEntry, LogLevel } from '../../api/types';
import { fmtTime } from '../../lib/format';
import { drawerTab, fail, modal } from '../../state/ui';
import { Button, Check, SearchBox } from '../../ui/controls';

const MaxEntries = 5000;
const MaxRendered = 1500;

const entries = signal<LogEntry[]>([]);
/** Errors that arrived while the logs tab was not shown. */
export const unseenErrors = signal(0);

type Filter = 'error' | 'warn' | 'info' | 'debug';
const filterOf: Record<LogLevel, Filter> = { fatal: 'error', error: 'error', warn: 'warn', message: 'info', info: 'info', debug: 'debug' };
const labels: Record<Filter, string> = { error: '错误', warn: '警告', info: '信息', debug: '调试' };
const levelText: Record<LogLevel, string> = { fatal: 'FATAL', error: 'ERR', warn: 'WARN', message: 'MSG', info: 'INFO', debug: 'DBG' };

let running = false;

/** Tails /api/logs with long polls for as long as the page is signed in. */
async function tail() {
  if (running) return;
  running = true;
  let cursor = -1;
  while (token.value) {
    try {
      const page = await api.logs(cursor, cursor < 0 ? 0 : 25);
      cursor = page.cursor;
      const fresh = page.entries ?? [];
      if (fresh.length) {
        entries.value = [...entries.value, ...fresh].slice(-MaxEntries);
        if (drawerTab.value !== 'logs') unseenErrors.value += fresh.filter(e => e.level === 'error' || e.level === 'fatal').length;
      }
    } catch {
      await new Promise(r => setTimeout(r, 3000));
    }
  }
  running = false;
}

export function LogsPanel() {
  const [levels, setLevels] = useState(new Set<Filter>(['error', 'warn', 'info']));
  const [source, setSource] = useState('');
  const [text, setText] = useState('');
  const [follow, setFollow] = useState(true);
  const list = useRef<HTMLDivElement>(null);

  useEffect(() => { tail(); }, [token.value]);
  useEffect(() => { if (drawerTab.value === 'logs') unseenErrors.value = 0; }, [drawerTab.value, entries.value]);

  const sources = useMemo(() => [...new Set(entries.value.map(e => e.source))].sort(), [entries.value.length]);
  const shown = useMemo(() => {
    const needle = text.toLowerCase();
    return entries.value
      .filter(e => levels.has(filterOf[e.level]) && (!source || e.source === source) && (!needle || e.text.toLowerCase().includes(needle)))
      .slice(-MaxRendered);
  }, [entries.value, levels, source, text]);

  useLayoutEffect(() => {
    if (follow && list.current) list.current.scrollTop = list.current.scrollHeight;
  }, [shown, follow]);

  const toggleLevel = (level: Filter) => setLevels(s => {
    const next = new Set(s);
    next.has(level) ? next.delete(level) : next.add(level);
    return next;
  });

  const openFile = async () => {
    try {
      const content = await api.logFile();
      modal.value = { title: '完整日志', wide: true, body: <pre class="log-file">{content}</pre> };
    } catch (e) {
      fail(e);
    }
  };

  return (
    <>
      <div class="log-toolbar">
        <div class="segmented multi">
          {(Object.keys(labels) as Filter[]).map(l => (
            <button key={l} type="button" class={levels.has(l) ? 'active' : ''} onClick={() => toggleLevel(l)}>{labels[l]}</button>
          ))}
        </div>
        <select class="select" value={source} onChange={e => setSource((e.target as HTMLSelectElement).value)}>
          <option value="">全部来源</option>
          {sources.map(s => <option key={s} value={s}>{s}</option>)}
        </select>
        <SearchBox small value={text} onInput={setText} placeholder="过滤日志" />
        <span class="spacer" />
        <Check checked={follow} onChange={setFollow} label="跟随" />
        <Button kind="ghost" icon="external" title="打开完整的日志文件" onClick={openFile}>完整日志</Button>
        <Button kind="ghost" icon="trash" onClick={() => (entries.value = [])}>清空</Button>
      </div>
      <div class="log-list scroll mono" ref={list}
        onScroll={e => { const el = e.currentTarget; if (el.scrollHeight - el.scrollTop - el.clientHeight > 40) setFollow(false); }}>
        {shown.length === 0 && <div class="log-sep">没有符合条件的日志</div>}
        {shown.map(e => <LogLine key={e.seq} entry={e} />)}
      </div>
    </>
  );
}

function LogLine({ entry: e }: { entry: LogEntry }) {
  const [expanded, setExpanded] = useState(false);
  const long = e.text.length > 400 || e.text.split('\n').length > 4;
  return (
    <div class={`lg l-${e.level}`}>
      <span class="tm">{fmtTime(e.time).slice(0, 8)}</span>
      <span class="lv">{levelText[e.level]}</span>
      <span class="src" title={e.source}>{e.source}</span>
      <span class={`msg${long && !expanded ? ' clamp' : ''}`} onClick={() => long && setExpanded(!expanded)}>{e.text}</span>
    </div>
  );
}
