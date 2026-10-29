import { useState } from 'preact/hooks';
import { signIn } from '../../api/client';
import { store } from '../../lib/store';
import { Mark } from '../../ui/Icon';
import { Check } from '../../ui/controls';

export function Login() {
  const [value, setValue] = useState('');
  const [remember, setRemember] = useState(false);
  // A stored token that brought us back here was rejected.
  const rejected = store.get('token', null, 'session') != null;

  return (
    <div class="overlay">
      <form class="login-card" autocomplete="off" onSubmit={e => { e.preventDefault(); if (value.trim()) signIn(value.trim(), remember); }}>
        <Mark class="login-mark" />
        <h1>Unity Runtime Debugger</h1>
        <p class="muted">token 见游戏日志中的 <code>UnityRuntimeDebugger token</code>。</p>
        <input class="mono" type="password" placeholder="token" spellcheck={false} autocomplete="off" autofocus value={value}
          onInput={e => setValue((e.target as HTMLInputElement).value)} />
        <Check checked={remember} onChange={setRemember} label="记住" />
        {rejected && <p class="login-error">token 无效</p>}
        <button class="btn primary" type="submit">连接</button>
      </form>
    </div>
  );
}
