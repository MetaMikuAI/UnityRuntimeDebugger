import { Fragment } from 'preact';
import { useEffect, useMemo, useState } from 'preact/hooks';
import { api, describe } from '../../api/client';
import type { ComponentEntry, Inspection, Method, Value as V } from '../../api/types';
import { lastSegment } from '../../lib/format';
import { insertIntoConsole } from '../../state/editor';
import { inspect } from '../../state/nav';
import { fail } from '../../state/ui';
import { Icon } from '../../ui/Icon';
import { Button, Check, Empty, IconButton, SearchBox, Spinner } from '../../ui/controls';
import { Expansion, ItemList, MemberList, ValueRow, ValueTree, valueMenu } from '../../ui/ValueTree';
import { openHookDialog } from '../hooks/HookDialog';

export function MembersTab({ data, version, statics, setStatics }: { data: Inspection; version: number; statics: boolean; setStatics: (v: boolean) => void }) {
  const [filter, setFilter] = useState('');
  const [inherited, setInherited] = useState(true);
  const members = data.members.filter(m => (inherited || m.depth === 0) && (!filter || m.name.toLowerCase().includes(filter.toLowerCase())));
  const hasInherited = data.members.some(m => m.depth > 0);

  return (
    <>
      <div class="obj-toolbar">
        <SearchBox small value={filter} onInput={setFilter} placeholder="过滤成员" />
        {hasInherited && <Check checked={inherited} onChange={setInherited} label="继承的成员" />}
        {data.static && <Check checked={statics} onChange={setStatics} label="读取静态属性" title="静态属性默认不读取：单例的 Instance 等 getter 可能会顺带创建对象" />}
        <span class="spacer" />
        <span class="muted legend"><i class="mk" /> 原生字段 <i class="mk managed" /> 托管字段 <i class="mk prop" /> 属性</span>
      </div>
      <div class="obj-pane">
        {members.length === 0 ? <div class="pane-note">{filter ? '没有匹配的成员' : '没有可显示的成员'}</div> : (
          <ValueTree version={version}>
            <MemberList members={members} owner={data.handle} grouped={hasInherited && inherited} />
          </ValueTree>
        )}
      </div>
    </>
  );
}

export function ComponentsTab({ handle, version }: { handle: number; version: number }) {
  const [components, setComponents] = useState<ComponentEntry[] | null>(null);
  const [open, setOpen] = useState(new Set<number>());

  const load = () => api.components(handle).then(setComponents, fail);
  useEffect(() => { load(); }, [handle, version]);

  if (!components) return <div class="pane-note"><Spinner /></div>;

  const setEnabled = async (c: ComponentEntry, enabled: boolean) => {
    try {
      await api.write(c.value.handle!, 'enabled', enabled);
      load();
    } catch (e) {
      fail(e);
    }
  };

  return (
    <ValueTree version={version} class="comp-list">
      {components.map((c, i) => {
        const h = c.value.handle;
        const isOpen = h != null && open.has(i);
        return (
          <div key={i} class={`comp-card${isOpen ? ' open' : ''}${c.missing ? ' missing' : ''}`}>
            <div class="comp-head" onClick={() => h != null && setOpen(s => { const n = new Set(s); n.has(i) ? n.delete(i) : n.add(i); return n; })}
              onContextMenu={e => valueMenu(e, c.value, c.value.type ?? '')}>
              <span class="caret"><Icon name="chev" /></span>
              {c.enabled != null && (
                <input type="checkbox" checked={c.enabled} title={c.enabled ? '停用组件' : '启用组件'}
                  onClick={e => e.stopPropagation()} onChange={e => setEnabled(c, (e.target as HTMLInputElement).checked)} />
              )}
              <span class="cname">{c.missing ? 'Missing script' : lastSegment(c.value.type)}</span>
              <div class="acts">
                {h != null && <IconButton small icon="dollar" title={`在控制台引用 $${h}`} onClick={() => insertIntoConsole('$' + h)} />}
                {h != null && <IconButton small icon="right" title="单独打开" onClick={() => inspect(h, c.value.type)} />}
              </div>
            </div>
            {isOpen && <div class="comp-body"><Expansion value={c.value} depth={0} path={`c${i}`} /></div>}
          </div>
        );
      })}
    </ValueTree>
  );
}

export function ItemsTab({ handle, version }: { handle: number; version: number }) {
  return (
    <div class="obj-pane">
      <ValueTree version={version}><ItemList handle={handle} depth={0} path="" /></ValueTree>
    </div>
  );
}

export function MethodsTab({ data }: { data: Inspection }) {
  const [methods, setMethods] = useState<Method[] | null>(null);
  const [filter, setFilter] = useState('');
  const [open, setOpen] = useState<number | null>(null);

  useEffect(() => { api.methods(data.handle).then(setMethods, fail); }, [data.handle]);

  const shown = useMemo(() => (methods ?? []).filter(m => !filter || m.name.toLowerCase().includes(filter.toLowerCase())), [methods, filter]);
  if (!methods) return <div class="pane-note"><Spinner /></div>;

  return (
    <>
      <div class="obj-toolbar">
        <SearchBox small value={filter} onInput={setFilter} placeholder="过滤方法" />
        <span class="muted">{shown.length} / {methods.length}</span>
      </div>
      <div class="obj-pane">
        {shown.length === 0 && <div class="pane-note">没有方法</div>}
        {shown.map(m => <MethodRow key={m.id} method={m} target={data.handle} open={open === m.id} toggle={() => setOpen(open === m.id ? null : m.id)} />)}
      </div>
    </>
  );
}

function MethodRow({ method: m, target, open, toggle }: { method: Method; target: number; open: boolean; toggle: () => void }) {
  const [args, setArgs] = useState<string[]>(() => m.parameters.map(p => (p.optional ? p.default ?? '' : '')));
  const [result, setResult] = useState<{ value?: V; error?: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const call = async () => {
    setBusy(true);
    try {
      // Trailing optional parameters left at their default are omitted, so the method's own default applies.
      let count = args.length;
      while (count > 0 && m.parameters[count - 1].optional && args[count - 1] === m.parameters[count - 1].default) count--;
      setResult({ value: await api.invoke(target, m.id, args.slice(0, count)) });
    } catch (e) {
      setResult({ error: describe(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div class={`m-row${m.invocable ? '' : ' disabled'}`}>
      <div class="m-head" onClick={toggle}>
        <span class="ret">{m.returnType}</span>
        <span class="mn">{m.name}</span>
        <span class="sig">({m.parameters.map((p, i) => <Fragment key={i}>{i > 0 && ', '}<span class="pt">{p.type}</span> <span class="pn">{p.name}</span></Fragment>)})</span>
        {m.static && <span class="st">S</span>}
        <span class="decl">{m.depth > 0 ? m.declaringType : ''}</span>
        <IconButton small icon="hook" title="Hook 这个方法" onClick={() => openHookDialog(m.ref)} />
      </div>
      {open && (
        <div class="m-form">
          {!m.invocable && <div class="muted">泛型方法、ref / out / 指针参数不能从这里调用，请用控制台。</div>}
          {m.parameters.map((p, i) => (
            <div key={p.name} class="m-param">
              <label title={p.type}><span class="pt">{p.type}</span>{p.name}</label>
              {p.editor === 'enum' ? (
                <select class="select" value={args[i]} onChange={e => setArgs(a => a.map((x, j) => (j === i ? (e.target as HTMLSelectElement).value : x)))}>
                  <option value="" />
                  {p.options?.map(o => <option key={o} value={o}>{o}</option>)}
                </select>
              ) : p.editor === 'bool' ? (
                <select class="select" value={args[i]} onChange={e => setArgs(a => a.map((x, j) => (j === i ? (e.target as HTMLSelectElement).value : x)))}>
                  <option value="" /><option value="true">true</option><option value="false">false</option>
                </select>
              ) : (
                <input class="input" spellcheck={false} value={args[i]} placeholder={p.optional ? `默认 ${p.default}` : p.editor ? p.editor : '值、null 或 $12'}
                  onInput={e => setArgs(a => a.map((x, j) => (j === i ? (e.target as HTMLInputElement).value : x)))}
                  onKeyDown={e => e.key === 'Enter' && call()} />
              )}
            </div>
          ))}
          <div class="m-actions">
            <Button kind="primary small" icon="play" disabled={!m.invocable || busy} onClick={call}>调用</Button>
            <Button kind="ghost small" icon="hook" onClick={() => openHookDialog(m.ref)}>Hook…</Button>
            <span class="muted mono">{m.ref}</span>
          </div>
          {result && (
            <div class="m-result">
              {result.error ? <div class="v-msg err">{result.error}</div> : <ValueTree><ValueRow name="结果" value={result.value} /></ValueTree>}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export function InstancesTab({ type }: { type: string }) {
  const [data, setData] = useState<{ total: number; items: V[] } | null>(null);
  const [filter, setFilter] = useState('');
  const load = () => api.instances(type).then(setData, fail);
  useEffect(() => { load(); }, [type]);

  if (!data) return <div class="pane-note"><Spinner /></div>;
  const items = data.items.filter(v => !filter || (v.text ?? '').toLowerCase().includes(filter.toLowerCase()));
  return (
    <>
      <div class="obj-toolbar">
        <SearchBox small value={filter} onInput={setFilter} placeholder="按名称过滤" />
        <span class="muted">{data.total > data.items.length ? `显示 ${data.items.length} / ${data.total}` : `${data.total} 个`}</span>
        <span class="spacer" />
        <IconButton icon="refresh" title="重新查找" onClick={load} />
      </div>
      <div class="obj-pane">
        {items.length === 0 ? <Empty>没有已加载的实例</Empty> : (
          <ValueTree>{items.map((v, i) => <ValueRow key={v.handle ?? i} name={`[${i}]`} value={v} path={`i${v.handle}`} />)}</ValueTree>
        )}
      </div>
    </>
  );
}

export function ImageTab({ handle, name }: { handle: number; name: string }) {
  const [max, setMax] = useState(1024);
  const [image, setImage] = useState<{ url: string; width: number; height: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pixel, setPixel] = useState(false);

  useEffect(() => {
    let url = '';
    setImage(null);
    api.image(handle, max).then(blob => {
      url = URL.createObjectURL(blob);
      const img = new Image();
      img.onload = () => setImage({ url, width: img.naturalWidth, height: img.naturalHeight });
      img.src = url;
    }, e => setError(describe(e)));
    return () => url && URL.revokeObjectURL(url);
  }, [handle, max]);

  if (error) return <div class="pane-note err">{error}</div>;
  return (
    <div class="preview">
      <div class="preview-meta">
        {[256, 1024, 4096].map(size => <Button key={size} kind={size === max ? 'small' : 'ghost small'} onClick={() => setMax(size)}>{size === 4096 ? '原尺寸' : `≤ ${size}px`}</Button>)}
        <Check checked={pixel} onChange={setPixel} label="像素风缩放" />
        {image && <span class="mono">{image.width} × {image.height}</span>}
        {image && <a class="btn small" href={image.url} download={`${name}.png`}><Icon name="download" />下载 PNG</a>}
      </div>
      <div class={`preview-stage${pixel ? ' pixel' : ''}`}>{image ? <img src={image.url} alt={name} /> : <Spinner />}</div>
    </div>
  );
}
