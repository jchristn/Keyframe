// Inline stroke icon set (24px grid). Icons are decorative by default; the surrounding
// button carries the accessible label.
const PATHS = {
  home: 'M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z',
  image: 'M4 5h16v14H4z M8.5 10a1.5 1.5 0 1 0 0-.01 M4 17l5-5 4 4 3-3 4 4',
  video: 'M3 6h13v12H3z M16 10l5-3v10l-5-3',
  jobs: 'M12 3l9 5-9 5-9-5z M3 13l9 5 9-5 M3 17.5l9 5 9-5',
  endpoints: 'M4 4h16v6H4z M4 14h16v6H4z M8 7h.01 M8 17h.01',
  history: 'M3 12a9 9 0 1 0 3-6.7 M3 4v5h5 M12 7v5l3 3',
  explorer: 'M4 5h16v14H4z M7 10l3 2-3 2 M12 15h5',
  settings:
    'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 16v-5 M12 8h.01',
  logout: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9',
  sun: 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z M12 1v2 M12 21v2 M4.2 4.2l1.4 1.4 M18.4 18.4l1.4 1.4 M1 12h2 M21 12h2 M4.2 19.8l1.4-1.4 M18.4 5.6l1.4-1.4',
  moon: 'M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z',
  github:
    'M9 19c-5 1.5-5-2.5-7-3 M16 22v-3.9a3.4 3.4 0 0 0-.9-2.6c3.1-.3 6.4-1.5 6.4-7A5.4 5.4 0 0 0 20 4.8 5 5 0 0 0 19.9 1S18.7.7 16 2.5a13.4 13.4 0 0 0-7 0C6.3.7 5.1 1 5.1 1A5 5 0 0 0 5 4.8a5.4 5.4 0 0 0-1.5 3.7c0 5.4 3.3 6.6 6.4 7A3.4 3.4 0 0 0 9 18.1V22',
  refresh: 'M21 12a9 9 0 1 1-2.6-6.4 M21 3v6h-6',
  copy: 'M9 9h11v11H9z M5 15H4V4h11v1',
  check: 'M5 12l5 5 9-11',
  close: 'M6 6l12 12 M18 6L6 18',
  alert: 'M12 3l10 18H2z M12 10v4 M12 17h.01',
  menu: 'M3 6h18 M3 12h18 M3 18h18',
  chevronLeft: 'M15 6l-6 6 6 6',
  chevronRight: 'M9 6l6 6-6 6',
  chevronDown: 'M6 9l6 6 6-6',
  chevronUp: 'M6 15l6-6 6 6',
  first: 'M17 6l-6 6 6 6 M7 6v12',
  last: 'M7 6l6 6-6 6 M17 6v12',
  plus: 'M12 5v14 M5 12h14',
  trash: 'M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13 M10 11v6 M14 11v6',
  eye: 'M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  code: 'M8 7l-5 5 5 5 M16 7l5 5-5 5',
  download: 'M12 3v12 M7 10l5 5 5-5 M4 19h16',
  upload: 'M12 21V9 M7 14l5-5 5 5 M4 5h16',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z M21 21l-5-5',
  more: 'M12 6h.01 M12 12h.01 M12 18h.01',
  play: 'M7 4l13 8-13 8z',
  stop: 'M6 6h12v12H6z',
  columns: 'M4 4h16v16H4z M10 4v16 M15 4v16',
  filter: 'M3 5h18l-7 8v6l-4 2v-8z',
  external: 'M14 4h6v6 M20 4l-9 9 M18 14v6H4V6h6',
  arrowUp: 'M12 19V5 M6 11l6-6 6 6',
  arrowDown: 'M12 5v14 M6 13l6 6 6-6',
  duplicate: 'M8 8h12v12H8z M4 16V4h12',
  globe: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M2 12h20 M12 2a15 15 0 0 1 0 20 M12 2a15 15 0 0 0 0 20',
  key: 'M15 7a4 4 0 1 1-3.9 5H3v3h3v3h3v-3h2.1A4 4 0 0 1 15 7z',
  server: 'M3 4h18v7H3z M3 13h18v7H3z M7 7.5h.01 M7 16.5h.01',
  activity: 'M3 12h4l3 8 4-16 3 8h4',
  zap: 'M13 2L4 14h7l-1 8 9-12h-7z',
  film: 'M4 3h16v18H4z M8 3v18 M16 3v18 M4 8h4 M4 13h4 M4 17h4 M16 8h4 M16 13h4 M16 17h4',
  save: 'M5 3h12l4 4v14H5z M8 3v6h8 M8 21v-7h8v7',
  power: 'M12 2v10 M18.4 6.6a9 9 0 1 1-12.8 0',
  lock: 'M5 11h14v10H5z M8 11V7a4 4 0 0 1 8 0v4',
  grip: 'M9 6h.01 M15 6h.01 M9 12h.01 M15 12h.01 M9 18h.01 M15 18h.01',
  palette: 'M12 3a9 9 0 1 0 0 18c1.1 0 1.6-.8 1.6-1.6 0-.5-.2-.8-.5-1.1-.3-.3-.5-.7-.5-1.1 0-.9.7-1.6 1.6-1.6H16a5 5 0 0 0 5-5c0-4-4-7.6-9-7.6z M7.5 11h.01 M10 7.5h.01 M14 7.5h.01 M16.5 11h.01',
  monitor: 'M3 4h18v12H3z M8 20h8 M12 16v4',
  analytics: 'M4 20V10 M10 20V4 M16 20v-7 M22 20H2',
  audio: 'M4 10v4 M8 7v10 M12 4v16 M16 8v8 M20 11v2',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 6v6l4 2',
  gauge: 'M12 14l4-4 M3.3 18a10 10 0 1 1 17.4 0'
};

function Icon({ name, size = 16, className = '' }) {
  const d = PATHS[name] || PATHS.info;
  return (
    <svg
      className={'icon ' + className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {d.split(' M').map((seg, i) => (
        <path key={i} d={i === 0 ? seg : 'M' + seg} />
      ))}
    </svg>
  );
}

export default Icon;
