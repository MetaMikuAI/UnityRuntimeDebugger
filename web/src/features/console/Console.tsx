import { insertNewlineAndIndent } from '@codemirror/commands';
import type { EditorView } from '@codemirror/view';
import { signal } from '@preact/signals';
import { Fragment } from 'preact';
import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import { api, describe } from '../../api/client';
import type { EvalResult } from '../../api/types';
import { isComplete, PROGRAM_TEMPLATE } from '../../lib/csharp';
import { store } from '../../lib/store';
import { registerConsole } from '../../state/editor';
import { snippets } from '../../state/session';
import { fail, modal, openMenu, toast } from '../../state/ui';
import { CodeEditor, showDiagnostics } from '../../ui/CodeEditor';
import { Icon } from '../../ui/Icon';
import { Button, IconButton, Segmented } from '../../ui/controls';
import { ValueRow, ValueTree } from '../../ui/ValueTree';
import { varsVersion } from '../session/SessionPanel';

type Mode = 'script' | 'program';

interface Entry {
  id: number;
  code: string;
  mode: Mode;
  result?: EvalResult;
  failure?: string;
}

const entries = signal<Entry[]>([]);
let entryId = 0;
const MaxHistory = 200;

/** Submissions run one after another in the order they were made, even while one is still running. */
let queue: Promise<unknown> = Promise.resolve();
const pending = signal(0);

export function Console() {
  const [mode, setModeState] = useState<Mode>(store.get('consoleMode', 'script'));
  const view = useRef<EditorView | null>(null);
  const output = useRef<HTMLDivElement>(null);
  const historyList = useRef<string[]>(store.get('history', []));
  const historyAt = useRef(-1);
  const state = useRef({ mode });
  state.current = { mode };
  const busy = pending.value > 0;

  const code = () => view.current?.state.doc.toString() ?? '';
  const setCode = (text: string) => {
    const v = view.current;
    v?.dispatch({ changes: { from: 0, to: v.state.doc.length, insert: text }, selection: { anchor: text.length } });
  };

  const setMode = (next: Mode) => {
    setModeState(next);
    store.set('consoleMode', next);
    if (next === 'program' && !code().trim()) setCode(PROGRAM_TEMPLATE);
    view.current?.focus();
  };

  const run = () => {
    const source = code();
    if (!source.trim()) return;
    const current = state.current.mode;
    const entry: Entry = { id: ++entryId, code: source, mode: current };
    entries.value = [...entries.value, entry];
    historyList.current = [source, ...historyList.current.filter(h => h !== source)].slice(0, MaxHistory);
    historyAt.current = -1;
    store.set('history', historyList.current);
    if (current === 'script') setCode('');

    pending.value++;
    queue = queue.then(async () => {
      try {
        const result = await api.eval(source, current);
        entries.value = entries.value.map(e => (e.id === entry.id ? { ...e, result } : e));
        if (current === 'program' && result.error?.diagnostics && view.current) showDiagnostics(view.current, result.error.diagnostics);
        if (current === 'script' && result.ok) varsVersion.bump();
      } catch (e) {
        entries.value = entries.value.map(x => (x.id === entry.id ? { ...x, failure: describe(e) } : x));
      } finally {
        pending.value--;
      }
    });
  };

  const recall = (step: number) => {
    const list = historyList.current;
    const next = historyAt.current + step;
    if (next < -1 || next >= list.length) return false;
    historyAt.current = next;
    setCode(next === -1 ? '' : list[next]);
    return true;
  };

  useEffect(() => {
    registerConsole({
      insert: text => {
        const v = view.current;
        if (!v) return;
        v.dispatch(v.state.replaceSelection(text));
        v.focus();
      },
      setCode,
      focus: () => view.current?.focus(),
    });
    return () => registerConsole(null);
  }, []);

  useLayoutEffect(() => {
    const el = output.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [entries.value]);

  const keys = [
    {
      key: 'Enter',
      run: (v: EditorView) => {
        const text = v.state.doc.toString();
        if (state.current.mode === 'script' && text.trim() && isComplete(text)) {
          run();
          return true;
        }
        return insertNewlineAndIndent(v);
      },
    },
    { key: 'Shift-Enter', run: insertNewlineAndIndent },
    { key: 'Mod-Enter', run: () => (run(), true) },
    {
      key: 'ArrowUp',
      run: (v: EditorView) => v.state.doc.lineAt(v.state.selection.main.head).number === 1 && recall(1),
    },
    {
      key: 'ArrowDown',
      run: (v: EditorView) => v.state.doc.lineAt(v.state.selection.main.head).number === v.state.doc.lines && recall(-1),
    },
    { key: 'Ctrl-l', run: () => ((entries.value = []), true) },
  ];

  const snippetMenu = (e: MouseEvent) => openMenu(e, [
    ...snippets.value.map(s => ({ label: s.name, icon: 'code', action: () => setCode(s.code) })),
    ...(snippets.value.length ? ['separator' as const] : []),
    {
      label: '把当前代码存为片段', icon: 'plus', action: () => {
        const text = code();
        if (!text.trim()) return toast('编辑器是空的');
        const name = prompt('片段名称', text.split('\n')[0].slice(0, 40));
        if (name) snippets.value = [...snippets.value.filter(s => s.name !== name), { name, code: text }];
      },
    },
    ...(snippets.value.length ? [{ label: '管理片段…', icon: 'edit', action: manageSnippets }] : []),
  ]);

  return (
    <>
      <div class="console-out scroll" ref={output}>
        {entries.value.length === 0 && (
          <div class="c-entry sys"><span class="gut" /><span><code>$12</code> 引用对象，<code>$_</code> 为上次结果</span></div>
        )}
        {entries.value.map(e => <ConsoleEntry key={e.id} entry={e} onRecall={() => { setMode(e.mode); setCode(e.code); view.current?.focus(); }} />)}
      </div>
      <div class="console-in">
        <div class="console-bar">
          <Segmented value={mode} onChange={setMode} options={[
            { id: 'script', label: '脚本', title: '交互式：变量跨提交保留' },
            { id: 'program', label: '程序', title: '完整程序：执行 public static object Execute()' },
          ]} />
          <span class="hint">{mode === 'script' ? 'Enter 执行 · Shift+Enter 换行 · ↑↓ 历史' : 'Ctrl+Enter 执行 · public static object Execute()'}</span>
          <span class="spacer" />
          <Button kind="ghost" icon="code" onClick={snippetMenu}>片段</Button>
          <Button kind="ghost" icon="rotate" title="清空会话变量" onClick={async () => {
            try {
              await api.resetSession();
              varsVersion.bump();
              toast('会话已重置', 'ok');
            } catch (e) {
              fail(e);
            }
          }}>重置会话</Button>
          <Button kind="ghost" icon="trash" title="清空输出 (Ctrl+L)" onClick={() => (entries.value = [])}>清空</Button>
        </div>
        <div class="editor">
          <CodeEditor class="console-editor" completion keys={keys} placeholder="C#"
            initial={mode === 'program' ? PROGRAM_TEMPLATE : ''} onReady={v => (view.current = v)} />
          <button type="button" class={`run-btn${busy ? ' busy' : ''}`} title="执行 (Enter / Ctrl+Enter)" onClick={run}><Icon name="play" /></button>
        </div>
      </div>
    </>
  );
}

function ConsoleEntry({ entry, onRecall }: { entry: Entry; onRecall: () => void }) {
  const r = entry.result;
  return (
    <>
      <div class="c-entry in">
        <span class="gut">{entry.mode === 'program' ? '#' : '›'}</span>
        <span class="code" title="点击放回编辑器" onClick={onRecall}>{entry.code}</span>
      </div>
      {entry.failure && <div class="c-entry err"><span class="gut">!</span><pre>{entry.failure}</pre></div>}
      {!r && !entry.failure && <div class="c-entry sys"><span class="gut" /><span class="spin" /></div>}
      {r?.output && <div class="c-entry out"><span class="gut" /><pre>{r.output}</pre></div>}
      {r?.ok && r.value && (
        <div class="c-entry res">
          <span class="gut">=</span>
          <ValueTree compact><ValueRow name={<span class="muted">{r.durationMs} ms</span>} value={r.value} path={'r' + entry.id} /></ValueTree>
        </div>
      )}
      {r?.ok && !r.value && !r.output && <div class="c-entry sys"><span class="gut">✓</span><span class="muted">完成 · {r.durationMs} ms</span></div>}
      {r?.error && (
        <div class="c-entry err">
          <span class="gut">!</span>
          <div>
            {r.error.diagnostics?.length ? (
              <div class="diag">
                {r.error.diagnostics.map((d, i) => <Fragment key={i}><span class="pos">{d.line}:{d.column}</span><span class="id">{d.id}</span><span class="msg">{d.message}</span></Fragment>)}
              </div>
            ) : <pre>{r.error.message}</pre>}
            {r.error.detail && <details><summary>堆栈</summary><pre>{r.error.detail}</pre></details>}
          </div>
        </div>
      )}
    </>
  );
}

function manageSnippets() {
  const Body = () => (
    <div class="snippet-list">
      {snippets.value.length === 0 && <p class="muted">没有片段</p>}
      {snippets.value.map(s => (
        <div key={s.name} class="snippet">
          <div class="snippet-head"><b>{s.name}</b><IconButton small icon="trash" title="删除" onClick={() => (snippets.value = snippets.value.filter(x => x.name !== s.name))} /></div>
          <pre>{s.code}</pre>
        </div>
      ))}
    </div>
  );
  modal.value = { title: '代码片段', body: <Body /> };
}
