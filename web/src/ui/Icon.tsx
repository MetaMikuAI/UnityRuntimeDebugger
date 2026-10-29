import type { JSX } from 'preact';

// Stroke icons on a 24×24 grid.
const paths: Record<string, JSX.Element> = {
  search: <><circle cx="11" cy="11" r="6.5" /><path d="m20 20-4.2-4.2" /></>,
  layers: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 13 9 5 9-5" /></>,
  braces: <path d="M8 4H7a2 2 0 0 0-2 2v4a2 2 0 0 1-2 2 2 2 0 0 1 2 2v4a2 2 0 0 0 2 2h1M16 4h1a2 2 0 0 1 2 2v4a2 2 0 0 0 2 2 2 2 0 0 0-2 2v4a2 2 0 0 1-2 2h-1" />,
  bookmark: <path d="M6 3.5h12v17l-6-4-6 4v-17Z" />,
  refresh: <path d="M20 11a8 8 0 0 0-14.6-4.5M4 4v3.5h3.5M4 13a8 8 0 0 0 14.6 4.5M20 20v-3.5h-3.5" />,
  left: <path d="m15 5-7 7 7 7" />,
  right: <path d="m9 5 7 7-7 7" />,
  chev: <path d="m9 6 6 6-6 6" />,
  terminal: <><rect x="3" y="4" width="18" height="16" rx="3" /><path d="m7.5 9.5 3 2.5-3 2.5M13 15h4" /></>,
  file: <><path d="M6 3h8l4 4v14H6V3Z" /><path d="M9 12h6M9 16h6" /></>,
  maximize: <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" />,
  code: <path d="m8 7-5 5 5 5M16 7l5 5-5 5M13.5 4l-3 16" />,
  rotate: <path d="M3.5 12a8.5 8.5 0 1 0 2.6-6.1M3.5 4v4.5H8" />,
  trash: <path d="M4 7h16M9 7V4h6v3M6.5 7l1 13h9l1-13" />,
  play: <path d="M7 4.5v15l12-7.5-12-7.5Z" fill="currentColor" stroke="none" />,
  pause: <path d="M8 5v14M16 5v14" />,
  external: <path d="M14 4h6v6M20 4l-9 9M18 14v6H4V6h6" />,
  x: <path d="m6 6 12 12M18 6 6 18" />,
  contrast: <><circle cx="12" cy="12" r="8.5" /><path d="M12 3.5v17a8.5 8.5 0 0 0 0-17Z" fill="currentColor" /></>,
  help: <><circle cx="12" cy="12" r="8.5" /><path d="M9.6 9.4a2.5 2.5 0 1 1 3.4 2.3c-.6.3-1 .8-1 1.5v.6M12 16.8v.2" /></>,
  cube: <><path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z" /><path d="m4 7.5 8 4.5 8-4.5M12 12v9" /></>,
  box: <rect x="4" y="4" width="16" height="16" rx="3" />,
  list: <path d="M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01" />,
  pin: <path d="M9 3h6l-1 6 4 3v2H6v-2l4-3-1-6ZM12 14v7" />,
  copy: <><rect x="8" y="8" width="12" height="12" rx="2" /><path d="M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3" /></>,
  dollar: <path d="m5 7 5 5-5 5M12 17h7" />,
  image: <><rect x="3.5" y="4.5" width="17" height="15" rx="2.5" /><circle cx="9" cy="10" r="1.6" /><path d="m20 16-4.5-4.5L7 19.5" /></>,
  eye: <><path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12Z" /><circle cx="12" cy="12" r="2.8" /></>,
  bolt: <path d="M13 3 5 13.5h6L10 21l8-10.5h-6L13 3Z" />,
  scene: <><rect x="3" y="5" width="18" height="14" rx="2.5" /><path d="M3 9h18" /></>,
  plus: <path d="M12 5v14M5 12h14" />,
  edit: <path d="M4 20h4L19 9l-4-4L4 16v4Z" />,
  download: <path d="M12 4v11M7 10l5 5 5-5M5 20h14" />,
  package: <><path d="M4 7.5 12 3l8 4.5v9L12 21l-8-4.5v-9Z" /><path d="M8 5.3 16 9.8" /></>,
  hook: <path d="M9 3v9a5 5 0 1 0 10 0v-1M9 3H6M16 9l3 2 2-3" />,
  warning: <><path d="M12 3.5 21 19.5H3L12 3.5Z" /><path d="M12 10v4M12 17v.2" /></>,
  method: <path d="M5 8h4l3 8h4M14 8h5M16 5l3 3-3 3" />,
};

export function Icon({ name, class: cls = 'i' }: { name: string; class?: string }) {
  return (
    <svg class={cls} viewBox="0 0 24 24" aria-hidden="true">
      {paths[name]}
    </svg>
  );
}

/** The brand mark: a gradient tile with code brackets. */
export function Mark({ class: cls }: { class: string }) {
  return (
    <svg class={cls} viewBox="0 0 24 24" aria-hidden="true">
      <defs>
        <linearGradient id="mark-gradient" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#9aa5ff" />
          <stop offset="1" stop-color="#f29ab3" />
        </linearGradient>
      </defs>
      <rect x="2.5" y="2.5" width="19" height="19" rx="6" fill="url(#mark-gradient)" />
      <path d="M9.2 8.2 6.4 12l2.8 3.8M14.8 8.2l2.8 3.8-2.8 3.8" fill="none" stroke="#0d0f14" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  );
}
