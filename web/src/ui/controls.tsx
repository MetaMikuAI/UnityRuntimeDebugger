import type { ComponentChildren, JSX } from 'preact';
import { Icon } from './Icon';

export function IconButton({ icon, title, onClick, small, on, disabled, class: cls = '' }: {
  icon: string;
  title: string;
  onClick: (e: MouseEvent) => void;
  small?: boolean;
  on?: boolean;
  disabled?: boolean;
  class?: string;
}) {
  return (
    <button type="button" class={`icon-btn${small ? ' sm' : ''}${on ? ' on' : ''} ${cls}`} title={title} aria-label={title} disabled={disabled}
      onClick={e => { e.stopPropagation(); onClick(e); }}>
      <Icon name={icon} />
    </button>
  );
}

export function Button({ icon, children, onClick, kind = '', title, disabled, type = 'button' }: {
  icon?: string;
  children?: ComponentChildren;
  onClick?: (e: MouseEvent) => void;
  kind?: '' | 'ghost' | 'primary' | 'small' | 'ghost small' | 'primary small';
  title?: string;
  disabled?: boolean;
  type?: 'button' | 'submit';
}) {
  return (
    <button type={type} class={`btn ${kind}`} title={title} disabled={disabled} onClick={onClick}>
      {icon && <Icon name={icon} />}
      {children}
    </button>
  );
}

export function Tabs<T extends string>({ value, onChange, tabs, class: cls = '', children }: {
  value: T;
  onChange: (value: T) => void;
  tabs: { id: T; label: string; icon?: string; count?: number | string; badge?: number }[];
  class?: string;
  children?: ComponentChildren;
}) {
  return (
    <nav class={`tabs ${cls}`} role="tablist">
      {tabs.map(t => (
        <button key={t.id} type="button" role="tab" aria-selected={t.id === value} class={`tab${t.id === value ? ' active' : ''}`} onClick={() => onChange(t.id)}>
          {t.icon && <Icon name={t.icon} />}
          {t.label}
          {t.count != null && <span class="count">{t.count}</span>}
          {!!t.badge && <span class="badge">{t.badge > 99 ? '99+' : t.badge}</span>}
        </button>
      ))}
      {children}
    </nav>
  );
}

export function Segmented<T extends string>({ value, options, onChange, title }: {
  value: T;
  options: { id: T; label: string; title?: string }[];
  onChange: (value: T) => void;
  title?: string;
}) {
  return (
    <div class="segmented" title={title}>
      {options.map(o => (
        <button key={o.id} type="button" title={o.title} class={o.id === value ? 'active' : ''} onClick={() => onChange(o.id)}>{o.label}</button>
      ))}
    </div>
  );
}

export function SearchBox({ value, onInput, placeholder, small, inputRef, onKeyDown }: {
  value: string;
  onInput: (value: string) => void;
  placeholder: string;
  small?: boolean;
  inputRef?: preact.Ref<HTMLInputElement>;
  onKeyDown?: (e: KeyboardEvent) => void;
}) {
  return (
    <label class={`search${small ? ' small' : ''}`}>
      <Icon name="search" />
      <input ref={inputRef} type="search" value={value} placeholder={placeholder} spellcheck={false}
        onInput={e => onInput((e.target as HTMLInputElement).value)} onKeyDown={onKeyDown} />
    </label>
  );
}

export function Switch({ checked, onChange, label, title }: { checked: boolean; onChange: (value: boolean) => void; label?: string; title?: string }) {
  return (
    <label class="switch" title={title} onClick={(e: JSX.TargetedMouseEvent<HTMLLabelElement>) => e.stopPropagation()}>
      <input type="checkbox" checked={checked} onChange={e => onChange((e.target as HTMLInputElement).checked)} />
      <span class="track" />
      {label && <span>{label}</span>}
    </label>
  );
}

export function Check({ checked, onChange, label, title }: { checked: boolean; onChange: (value: boolean) => void; label: string; title?: string }) {
  return (
    <label class="check" title={title}>
      <input type="checkbox" checked={checked} onChange={e => onChange((e.target as HTMLInputElement).checked)} />
      <span>{label}</span>
    </label>
  );
}

export const Spinner = () => <span class="spin" />;

export const Empty = ({ children }: { children: ComponentChildren }) => <div class="t-empty">{children}</div>;
