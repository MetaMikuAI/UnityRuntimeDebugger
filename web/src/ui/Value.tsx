import type { Value as V } from '../api/types';
import { api } from '../api/client';
import { lastSegment } from '../lib/format';
import { inspect } from '../state/nav';
import { fail, modal } from '../state/ui';
import { copyText } from '../lib/format';
import { Button } from './controls';

function Swatch({ color }: { color: [number, number, number, number] }) {
  const [r, g, b, a] = color.map(x => Math.max(0, Math.min(1, x)));
  return <span class="swatch"><i style={{ background: `rgba(${(r * 255) | 0}, ${(g * 255) | 0}, ${(b * 255) | 0}, ${a})` }} /></span>;
}

async function showText(handle: number) {
  try {
    const data = await api.inspect(handle);
    const text = data.text ?? '';
    modal.value = {
      title: `字符串 · ${text.length} 字符`,
      body: <pre>{text}</pre>,
      actions: <Button kind="small" icon="copy" onClick={() => copyText(text)}>复制</Button>,
    };
  } catch (e) {
    fail(e);
  }
}

/** One value, inline. Values with a handle open in the inspector on click. */
export function Value({ value, link = true }: { value?: V; link?: boolean }) {
  const v = value ?? { kind: 'null' };
  const open = link && v.handle != null && v.kind !== 'string'
    ? (e: MouseEvent) => { e.stopPropagation(); inspect(v.handle!, v.text || v.type); }
    : undefined;

  let body;
  let title = v.type;
  switch (v.kind) {
    case 'null':
      body = v.type ?? 'null';
      break;
    case 'void':
      body = '无返回值';
      break;
    case 'string': {
      const quoted = JSON.stringify(v.text ?? '');
      title = `string · ${v.length ?? (v.text ?? '').length} 字符${v.truncated ? ' · 点击查看全文' : ''}`;
      return (
        <span class={`val k-string${v.truncated ? ' link' : ''}`} title={title}
          onClick={v.truncated && v.handle ? e => { e.stopPropagation(); showText(v.handle!); } : undefined}>
          {v.truncated ? quoted.slice(0, -1) + '…"' : quoted}
        </span>
      );
    }
    case 'char':
      body = `'${v.text}'`;
      break;
    case 'enum':
      body = <><span class="ep">{lastSegment(v.type)}.</span>{v.text}</>;
      break;
    case 'type':
      body = <><span class="n">type </span><span class="t">{v.text}</span></>;
      break;
    case 'destroyed':
      body = <><span class="t">{v.type}</span> destroyed</>;
      break;
    case 'error':
      body = '⚠ ' + (v.text ?? 'error');
      title = v.text;
      break;
    case 'struct':
      body = <>{v.color && <Swatch color={v.color} />}<span class="t">{v.type}</span>{v.text && <> <span class="p">{v.text}</span></>}</>;
      break;
    case 'object':
      body = (
        <>
          <span class="t">{v.type}</span>
          {v.text && <> <span class="p">“{v.text}”</span></>}
          {v.count != null && <> <span class="n">({v.count})</span></>}
        </>
      );
      break;
    default:
      body = v.text;
  }

  return (
    <span class={`val k-${v.kind}${open ? ' link' : ''}`} title={open ? `检查 $${v.handle}` : title} onClick={open}>
      {body}
    </span>
  );
}
