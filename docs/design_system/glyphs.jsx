// Vianigram — Segoe MDL2-style glyph set (minimal SVG icons)
// Drawn as 1.5px stroke geometric line glyphs to feel native to the system.

const G = (path, opts = {}) => ({ path, ...opts });

const GLYPHS = {
  // Navigation
  back:    'M14 6 L8 12 L14 18',
  forward: 'M10 6 L16 12 L10 18',
  chevron: 'M9 6 L15 12 L9 18',
  close:   'M6 6 L18 18 M18 6 L6 18',
  more:    'M5 12 h.01 M12 12 h.01 M19 12 h.01',
  ellipsis:'M4 12 h.01 M9 12 h.01 M14 12 h.01 M19 12 h.01',

  // Comms
  send:     'M3 12 L21 4 L17 21 L11 13 L3 12 Z',
  attach:   'M9 14 L15 8 a3 3 0 0 1 4.2 4.2 L11 20 a5 5 0 0 1 -7 -7 L13 4',
  mic:      'M12 4 a3 3 0 0 1 3 3 v5 a3 3 0 0 1 -6 0 V7 a3 3 0 0 1 3 -3 Z M6 11 a6 6 0 0 0 12 0 M12 17 v3',
  phone:    'M5 4 h4 l2 5 -2.5 1.5 a11 11 0 0 0 5 5 L15 13 l5 2 v4 a2 2 0 0 1 -2 2 A16 16 0 0 1 3 6 a2 2 0 0 1 2 -2 Z',
  video:    'M3 7 h12 v10 h-12 z M15 11 L21 7 v10 L15 13 z',
  search:   'M11 4 a7 7 0 1 1 0 14 a7 7 0 0 1 0 -14 Z M16 16 L21 21',
  edit:     'M4 20 L4 16 L16 4 L20 8 L8 20 Z',
  emoji:    'M12 4 a8 8 0 1 1 0 16 a8 8 0 0 1 0 -16 Z M9 10 v.01 M15 10 v.01 M9 14 q3 3 6 0',

  // List/util
  pin:       'M12 2 L9 6 L9 11 L6 14 H18 L15 11 V6 L12 2 Z M12 14 V22',
  mute:      'M3 9 v6 h4 l5 4 V5 L7 9 Z M16 9 L22 15 M22 9 L16 15',
  speaker:   'M3 9 v6 h4 l5 4 V5 L7 9 Z M16 8 a6 6 0 0 1 0 8 M19 5 a10 10 0 0 1 0 14',
  check:     'M5 12 L10 17 L20 7',
  doublecheck:'M3 12 L8 17 L17 7 M11 17 L13 15',
  star:      'M12 3 L14.5 9 L21 9.5 L16 14 L17.5 21 L12 17.5 L6.5 21 L8 14 L3 9.5 L9.5 9 Z',
  pinFilled: 'M12 2 L9 6 L9 11 L6 14 H18 L15 11 V6 L12 2 Z M12 14 V22',

  // Settings
  bell:      'M6 17 V11 a6 6 0 0 1 12 0 V17 L20 19 H4 Z M10 19 a2 2 0 0 0 4 0',
  lock:      'M7 11 V8 a5 5 0 0 1 10 0 V11 M5 11 H19 V21 H5 Z',
  database:  'M5 6 a7 3 0 0 0 14 0 a7 3 0 0 0 -14 0 Z M5 6 V18 a7 3 0 0 0 14 0 V6 M5 12 a7 3 0 0 0 14 0',
  chat:      'M4 5 H20 V17 H13 L8 21 V17 H4 Z',
  sticker:   'M4 4 H14 L20 10 V20 H4 Z M14 4 V10 H20',
  device:    'M3 5 H17 V15 H3 Z M5 18 H15 M9 15 V18',
  globe:     'M12 4 a8 8 0 1 1 0 16 a8 8 0 0 1 0 -16 Z M4 12 H20 M12 4 a8 14 0 0 1 0 16 M12 4 a8 14 0 0 0 0 16',
  user:      'M12 4 a4 4 0 1 1 0 8 a4 4 0 0 1 0 -8 Z M4 21 a8 8 0 0 1 16 0',
  users:     'M9 5 a3 3 0 1 1 0 6 a3 3 0 0 1 0 -6 Z M16 7 a2.5 2.5 0 1 1 0 5 M3 19 a6 6 0 0 1 12 0 M14 19 a4 4 0 0 1 7 0',
  channel:   'M3 9 V15 H7 L13 19 V5 L7 9 Z M16 8 a6 6 0 0 1 0 8',
  contacts:  'M5 4 H19 V20 H5 Z M9 9 a3 3 0 1 1 0 6 a3 3 0 0 1 0 -6 Z M14 12 H17 M14 16 H17',
  saved:     'M6 4 H18 V20 L12 16 L6 20 Z',
  call:      'M5 4 h4 l2 5 -2.5 1.5 a11 11 0 0 0 5 5 L15 13 l5 2 v4 a2 2 0 0 1 -2 2 A16 16 0 0 1 3 6 a2 2 0 0 1 2 -2 Z',
  shield:    'M12 3 L20 6 V12 a8 9 0 0 1 -8 9 a8 9 0 0 1 -8 -9 V6 Z',
  eye:       'M2 12 s4 -7 10 -7 s10 7 10 7 s-4 7 -10 7 s-10 -7 -10 -7 Z M12 9 a3 3 0 1 1 0 6 a3 3 0 0 1 0 -6 Z',
  trash:     'M6 7 H18 L17 21 H7 Z M9 7 V4 H15 V7 M10 11 V18 M14 11 V18',
  forwardArrow:'M4 12 H18 M14 7 L20 12 L14 17',
  share:    'M16 5 L21 10 L16 15 M21 10 H10 a6 6 0 0 0 -6 6 V19',
  download:'M12 4 V16 M7 11 L12 16 L17 11 M5 19 H19',
  qr:      'M4 4 H10 V10 H4 Z M14 4 H20 V10 H14 Z M4 14 H10 V20 H4 Z M14 14 H17 V17 H14 Z M19 14 H20 V15 H19 Z M14 19 H15 V20 H14 Z M19 19 H20 V20 H19 Z M6 6 H8 V8 H6 Z M16 6 H18 V8 H16 Z M6 16 H8 V18 H6 Z',
  play:    'M7 4 V20 L20 12 Z',
  pause:   'M7 5 H10 V19 H7 Z M14 5 H17 V19 H14 Z',
  plus:    'M12 4 V20 M4 12 H20',
  camera:  'M4 8 H8 L10 5 H14 L16 8 H20 V19 H4 Z M12 11 a3.5 3.5 0 1 1 0 7 a3.5 3.5 0 0 1 0 -7 Z',
  image:   'M4 5 H20 V19 H4 Z M4 15 L9 10 L14 15 L17 12 L20 15 M16 8 a1.5 1.5 0 1 1 0 3 a1.5 1.5 0 0 1 0 -3 Z',
  gif:     'M4 5 H20 V19 H4 Z M7 9 H11 V12 H9 M13 9 V15 M16 9 H20 M16 12 H19',
  mute2:   'M3 9 V15 H7 L13 19 V5 L7 9 Z M16 9 L22 15 M22 9 L16 15',
  endcall: 'M2 14 a14 8 0 0 1 20 0 L20 17 L17 16 L16 13 a8 4 0 0 0 -8 0 L7 16 L4 17 Z',
  acceptCall:'M5 4 h4 l2 5 -2.5 1.5 a11 11 0 0 0 5 5 L15 13 l5 2 v4 a2 2 0 0 1 -2 2 A16 16 0 0 1 3 6 a2 2 0 0 1 2 -2 Z',
  declineCall:'M5 4 h4 l2 5 -2.5 1.5 a11 11 0 0 0 5 5 L15 13 l5 2 v4 a2 2 0 0 1 -2 2 A16 16 0 0 1 3 6 a2 2 0 0 1 2 -2 Z',
  refresh: 'M4 12 a8 8 0 0 1 14 -5 L20 9 M20 4 V9 H15 M20 12 a8 8 0 0 1 -14 5 L4 15 M4 20 V15 H9',
  paint:   'M4 5 H20 V11 H4 Z M8 11 V14 H16 V11 M10 14 V19 H14 V14',
  sun:     'M12 7 a5 5 0 1 1 0 10 a5 5 0 0 1 0 -10 Z M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M5 5 L6.5 6.5 M17.5 17.5 L19 19 M5 19 L6.5 17.5 M17.5 6.5 L19 5',
  moon:    'M20 14 a8 8 0 0 1 -10 -10 a8 8 0 1 0 10 10 Z',
  reaction:'M12 4 a8 8 0 1 1 0 16 a8 8 0 0 1 0 -16 Z M9 14 q3 3 6 0',
  filterNew:'M5 5 H19 L14 12 V18 L10 20 V12 Z'
};

function Glyph({ name, size = 18, stroke = 1.5, color = 'currentColor', fill = 'none', style }) {
  const d = GLYPHS[name];
  if (!d) return null;
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" style={style} aria-hidden>
      <path d={d} stroke={color} strokeWidth={stroke} fill={fill} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

window.Glyph = Glyph;
window.GLYPHS = GLYPHS;
