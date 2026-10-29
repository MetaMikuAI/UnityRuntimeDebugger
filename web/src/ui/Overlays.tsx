import { useEffect, useLayoutEffect, useRef } from 'preact/hooks';
import { menu, modal, toasts } from '../state/ui';
import { Icon } from './Icon';
import { IconButton } from './controls';

export function Toasts() {
  return (
    <div class="toasts" aria-live="polite">
      {toasts.value.map(t => <div key={t.id} class={`toast ${t.kind}`}>{t.text}</div>)}
    </div>
  );
}

export function ContextMenu() {
  const ref = useRef<HTMLDivElement>(null);
  const state = menu.value;

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el || !state) return;
    // keep the menu on screen
    const { width, height } = el.getBoundingClientRect();
    el.style.left = Math.max(4, Math.min(state.x, innerWidth - width - 8)) + 'px';
    el.style.top = Math.max(4, Math.min(state.y, innerHeight - height - 8)) + 'px';
  }, [state]);

  useEffect(() => {
    if (!state) return;
    const close = () => (menu.value = null);
    addEventListener('pointerdown', close);
    addEventListener('blur', close);
    addEventListener('keydown', close);
    return () => {
      removeEventListener('pointerdown', close);
      removeEventListener('blur', close);
      removeEventListener('keydown', close);
    };
  }, [state]);

  if (!state) return null;
  return (
    <div class="menu" ref={ref} role="menu" onPointerDown={e => e.stopPropagation()}>
      {state.items.map((item, i) => item === 'separator'
        ? <div key={i} class="menu-sep" />
        : (
          <button key={i} type="button" role="menuitem" class="menu-item" onClick={() => { menu.value = null; item.action(); }}>
            {item.icon && <Icon name={item.icon} />}
            <span>{item.label}</span>
            {item.hint && <span class="sub">{item.hint}</span>}
          </button>
        ))}
    </div>
  );
}

export function Modal() {
  const content = modal.value;
  useEffect(() => {
    if (!content) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && (modal.value = null);
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, [content]);

  if (!content) return null;
  return (
    <div class="overlay modal-overlay" onPointerDown={e => e.target === e.currentTarget && (modal.value = null)}>
      <div class={`modal${content.wide ? ' wide' : ''}`} role="dialog" aria-label={content.title}>
        <div class="modal-head">
          <span>{content.title}</span>
          <span class="modal-actions">
            {content.actions}
            <IconButton icon="x" title="关闭" onClick={() => (modal.value = null)} />
          </span>
        </div>
        <div class="modal-body scroll">{content.body}</div>
      </div>
    </div>
  );
}
