/** Drag handle between panes; reports the pointer position while dragging. */
export function Resizer({ axis, onDrag }: { axis: 'x' | 'y'; onDrag: (x: number, y: number) => void }) {
  const start = (e: PointerEvent) => {
    e.preventDefault();
    const handle = e.currentTarget as HTMLElement;
    const container = handle.parentElement!.getBoundingClientRect();
    handle.classList.add('dragging');
    document.body.classList.add(axis === 'x' ? 'dragging-col' : 'dragging-row');
    const move = (m: PointerEvent) => onDrag(m.clientX - container.left, m.clientY);
    const stop = () => {
      handle.classList.remove('dragging');
      document.body.classList.remove('dragging-col', 'dragging-row');
      removeEventListener('pointermove', move);
      removeEventListener('pointerup', stop);
    };
    addEventListener('pointermove', move);
    addEventListener('pointerup', stop);
  };
  return <div class={`resizer ${axis === 'x' ? 'vertical' : 'horizontal'}`} title="拖动调整大小" onPointerDown={start} />;
}
