import type { EditorView } from '@codemirror/view';
import { useEffect, useRef, useState } from 'preact/hooks';
import { ApiError, api, describe } from '../../api/client';
import type { Diagnostic, HookAction, MethodCandidate } from '../../api/types';
import { fail, modal, showDrawer, toast } from '../../state/ui';
import { CodeEditor, showDiagnostics } from '../../ui/CodeEditor';
import { Icon } from '../../ui/Icon';
import { Button, Check, Segmented, Spinner } from '../../ui/controls';
import { refreshHooks, selectedHook } from './HooksPanel';

export function openHookDialog(reference = '') {
  modal.value = { title: '新建 Hook', body: <HookForm initial={reference} />, wide: true };
}

function HookForm({ initial }: { initial: string }) {
  const [reference, setReference] = useState(initial);
  const [candidates, setCandidates] = useState<MethodCandidate[] | null>(null);
  const [chosen, setChosen] = useState<string | null>(null);
  const [action, setAction] = useState<Exclude<HookAction, 'custom'>>('log');
  const [returnValue, setReturnValue] = useState('');
  const [acceptFolded, setAcceptFolded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const resolve = async (ref = reference) => {
    if (!ref.trim()) return;
    setError(null);
    setCandidates(null);
    try {
      const list = await api.candidates(ref.trim());
      setCandidates(list);
      setChosen(list.find(c => c.hookable)?.ref ?? null);
    } catch (e) {
      setError(describe(e));
      setCandidates([]);
    }
  };

  useEffect(() => { if (initial) resolve(initial); }, []);

  const candidate = candidates?.find(c => c.ref === chosen);
  const folded = candidate?.native?.folded ?? [];

  const create = async () => {
    if (!chosen) return;
    setBusy(true);
    setError(null);
    try {
      const hook = await api.createHook({ method: chosen, action, returnValue: action === 'return' ? returnValue : undefined, acceptFolded });
      modal.value = null;
      toast(`已挂钩 ${hook.declaringType}::${hook.signature}`, 'ok');
      await refreshHooks();
      selectedHook.value = hook.id;
      showDrawer('hooks');
    } catch (e) {
      setError(e instanceof ApiError && e.code === 'folded' ? '该方法与其他方法共用机器码（ICF），需要确认。' : describe(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form class="hook-form" onSubmit={e => { e.preventDefault(); resolve(); }}>
      <label class="field">
        <span>方法</span>
        <div class="field-row">
          <input class="input mono" value={reference} spellcheck={false} placeholder="Namespace.Type::Method 或 Type::Method(int, string)"
            onInput={e => setReference((e.target as HTMLInputElement).value)} />
          <Button type="submit" icon="search">解析</Button>
        </div>
      </label>

      {candidates == null && reference && !error && <Spinner />}
      {candidates && candidates.length > 0 && (
        <div class="candidates">
          {candidates.map(c => (
            <label key={c.ref} class={`candidate${c.hookable ? '' : ' disabled'}`}>
              <input type="radio" name="candidate" disabled={!c.hookable} checked={c.ref === chosen} onChange={() => { setChosen(c.ref); setAcceptFolded(false); }} />
              <span class="mono">{c.declaringType}::{c.signature}</span>
              {c.static && <span class="st">S</span>}
              {c.native?.location && <span class="muted mono">{c.native.location}</span>}
              {!c.hookable && <span class="muted">{c.reason}</span>}
            </label>
          ))}
        </div>
      )}

      {folded.length > 0 && (
        <div class="icf-warning">
          <div class="icf-title"><Icon name="warning" />ICF：{folded.length} 个方法共用同一份机器码</div>
          <p>其他类的调用按实例类型放行；静态方法和同类方法无法区分。</p>
          <ul class="mono">{folded.map(f => <li key={f}>{f}</li>)}</ul>
          <Check checked={acceptFolded} onChange={setAcceptFolded} label="仍然挂钩" />
        </div>
      )}

      <label class="field">
        <span>行为</span>
        <Segmented value={action} onChange={setAction} options={[
          { id: 'log', label: '记录调用', title: '原方法照常执行' },
          { id: 'skip', label: '跳过原方法', title: '不执行原方法（返回默认值）' },
          { id: 'return', label: '返回固定值', title: '不执行原方法，返回下面的 C# 表达式' },
        ]} />
      </label>
      {action === 'return' && (
        <label class="field">
          <span>返回值（C# 表达式）</span>
          <input class="input mono" value={returnValue} spellcheck={false} placeholder="例如 0、true、&quot;text&quot;、$12" onInput={e => setReturnValue((e.target as HTMLInputElement).value)} />
        </label>
      )}

      {error && <div class="form-error">{error}</div>}
      <div class="form-actions">
        <span class="spacer" />
        <Button onClick={() => (modal.value = null)}>取消</Button>
        <Button kind="primary" icon="hook" disabled={!chosen || busy || (folded.length > 0 && !acceptFolded) || (action === 'return' && !returnValue.trim())} onClick={create}>挂钩</Button>
      </div>
    </form>
  );
}

export async function openSourceEditor(id: number) {
  try {
    const hook = await api.hook(id);
    modal.value = { title: `Hook #${id} 补丁源码`, wide: true, body: <SourceForm id={id} source={hook.source ?? ''} /> };
  } catch (e) {
    fail(e);
  }
}

function SourceForm({ id, source }: { id: number; source: string }) {
  const view = useRef<EditorView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.updateHook(id, { source: view.current!.state.doc.toString() });
      modal.value = null;
      toast(`Hook #${id} 已更新`, 'ok');
      refreshHooks();
    } catch (e) {
      setError(describe(e));
      const diagnostics = (e as ApiError).details?.diagnostics as Diagnostic[] | undefined;
      if (diagnostics && view.current) showDiagnostics(view.current, diagnostics);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div class="source-form">
      <CodeEditor class="source-editor" numbers initial={source} onReady={v => (view.current = v)} keys={[{ key: 'Mod-s', run: () => (save(), true) }]} />
      {error && <div class="form-error">{error}</div>}
      <div class="form-actions">
        <span class="muted">Ctrl+S 保存</span>
        <span class="spacer" />
        <Button onClick={() => (modal.value = null)}>取消</Button>
        <Button kind="primary" disabled={busy} onClick={save}>保存</Button>
      </div>
    </div>
  );
}
