import { render } from 'preact';
import { signIn } from './api/client';
import { App } from './app';
import './styles/app.css';

// The startup log prints http://host:port/#token=...: take it, then drop it from the address bar.
const match = location.hash.match(/token=([^&]+)/);
if (match) {
  signIn(decodeURIComponent(match[1]), false);
  history.replaceState(null, '', location.pathname + location.search);
}

render(<App />, document.getElementById('root')!);
