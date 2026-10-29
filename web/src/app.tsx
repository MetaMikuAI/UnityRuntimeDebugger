import { useEffect } from 'preact/hooks';
import { token } from './api/client';
import { Login } from './features/shell/Login';
import { Shell } from './features/shell/Shell';
import { pollStatus, stopStatus } from './state/status';
import { ContextMenu, Modal, Toasts } from './ui/Overlays';

export function App() {
  const signedIn = token.value != null;
  useEffect(() => {
    if (!signedIn) return;
    pollStatus();
    return stopStatus;
  }, [signedIn, token.value]);

  return (
    <>
      {signedIn ? <Shell /> : <Login />}
      <Modal />
      <ContextMenu />
      <Toasts />
    </>
  );
}
