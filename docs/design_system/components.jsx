// Vianigram — Core components

const { useState, useEffect, useRef, useMemo, useCallback } = React;

// Deterministic avatar color from a string
const AVATAR_COLORS = [
  '#00ABA9', '#7E3878', '#E51400', '#A4C400',
  '#F09609', '#1BA1E2', '#60A917', '#AA00FF'
];
function colorForName(name) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
  return AVATAR_COLORS[h % AVATAR_COLORS.length];
}
function initials(name) {
  const parts = name.replace(/[^\w\s]/g, '').trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

// Avatar
function Avatar({ name, size = 56, src, accentTint }) {
  const bg = accentTint ? 'var(--vg-accent)' : colorForName(name || '?');
  return (
    <div className="vg-avatar" style={{ width: size, height: size, background: bg, fontSize: size * 0.4 }}>
      {src ? <img src={src} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : initials(name || '?')}
    </div>
  );
}

// Status bar — minimal, transparent
function StatusBar({ time = '9:41', signal = true, battery = 87 }) {
  return (
    <div className="vg-status">
      <div className="vg-status-left">
        <span style={{ opacity: 0.8 }}>{signal ? '••••' : ''}</span>
        <span style={{ opacity: 0.6, fontSize: 10 }}>vianet</span>
      </div>
      <div style={{ font: '300 14px/1 var(--vg-font)' }}>{time}</div>
      <div className="vg-status-right">
        <span style={{ fontSize: 10, opacity: 0.8 }}>{battery}%</span>
        <span style={{ display: 'inline-block', width: 16, height: 8, border: '1px solid currentColor', position: 'relative' }}>
          <span style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${battery}%`, background: 'currentColor' }} />
        </span>
      </div>
    </div>
  );
}

// Title bar — app name + page title
function TitleBar({ pageTitle, appName = 'VIANIGRAM' }) {
  return (
    <div className="vg-titlebar">
      <div className="vg-app-name">{appName}</div>
      {pageTitle != null && <h1 className="vg-page-title">{pageTitle}</h1>}
    </div>
  );
}

// Pivot — horizontal swipeable sections with peeking next-section header
function Pivot({ sections, active, onChange, children }) {
  const [animating, setAnimating] = useState(false);
  const [dragX, setDragX] = useState(0);
  const startX = useRef(null);
  const idx = sections.findIndex(s => s.id === active);

  const handleStart = (e) => {
    startX.current = (e.touches ? e.touches[0].clientX : e.clientX);
  };
  const handleMove = (e) => {
    if (startX.current == null) return;
    const x = (e.touches ? e.touches[0].clientX : e.clientX);
    setDragX(x - startX.current);
  };
  const handleEnd = () => {
    if (startX.current == null) return;
    const threshold = 60;
    if (dragX < -threshold && idx < sections.length - 1) onChange(sections[idx + 1].id);
    else if (dragX > threshold && idx > 0) onChange(sections[idx - 1].id);
    startX.current = null;
    setDragX(0);
  };

  // Render headers — show active + peeking-next for the swipeability cue
  return (
    <>
      <div className="vg-pivot-headers" style={{ transform: `translateX(${dragX * 0.4}px)`, transition: dragX === 0 ? 'transform 280ms cubic-bezier(.2,.7,.3,1)' : 'none' }}>
        {sections.map(s => (
          <button key={s.id}
            className={`vg-pivot-header${s.id === active ? ' active' : ''}`}
            onClick={() => onChange(s.id)}>
            {s.label}
          </button>
        ))}
      </div>
      <div className="vg-pivot-content"
        onMouseDown={handleStart} onMouseMove={handleMove} onMouseUp={handleEnd} onMouseLeave={handleEnd}
        onTouchStart={handleStart} onTouchMove={handleMove} onTouchEnd={handleEnd}
        style={{ transform: `translateX(${dragX * 0.6}px)`, transition: dragX === 0 ? 'transform 280ms cubic-bezier(.2,.7,.3,1)' : 'none' }}>
        {children}
      </div>
    </>
  );
}

// App bar (bottom)
function AppBar({ buttons = [], onMore }) {
  const visible = buttons.slice(0, 4);
  return (
    <div className="vg-appbar">
      {visible.map((b, i) => (
        <button key={i} className="vg-appbar-btn" onClick={b.onClick}>
          <Glyph name={b.glyph} size={20} />
          <span>{b.label}</span>
        </button>
      ))}
      {onMore && (
        <button className="vg-appbar-ellipsis" onClick={onMore}>
          • • •
        </button>
      )}
    </div>
  );
}

// Tilt-press wrapper — real perspective transform on press, signature WP feel
function Tilt({ children, onClick, style, className = '' }) {
  const [pressed, setPressed] = useState(false);
  const [tilt, setTilt] = useState({ tx: 0, ty: 0 });
  const ref = useRef(null);

  const start = (e) => {
    const rect = ref.current.getBoundingClientRect();
    const x = (e.touches ? e.touches[0].clientX : e.clientX) - rect.left;
    const y = (e.touches ? e.touches[0].clientY : e.clientY) - rect.top;
    const cx = rect.width / 2, cy = rect.height / 2;
    const ty = ((x - cx) / cx) * -3; // rotateY based on x offset (degrees)
    const tx = ((y - cy) / cy) * 3;  // rotateX based on y offset
    setTilt({ tx, ty });
    setPressed(true);
  };
  const end = () => { setPressed(false); setTilt({ tx: 0, ty: 0 }); };

  return (
    <div ref={ref}
      className={`vg-tilt ${className}${pressed ? ' pressed' : ''}`}
      style={{ ...style, '--tx': `${tilt.tx}deg`, '--ty': `${tilt.ty}deg` }}
      onMouseDown={start} onMouseUp={end} onMouseLeave={end}
      onTouchStart={start} onTouchEnd={end} onTouchCancel={end}
      onClick={onClick}>
      {children}
    </div>
  );
}

// Chat list row
function ChatRow({ chat, onClick }) {
  return (
    <Tilt className="vg-row" onClick={onClick}>
      <Avatar name={chat.name} src={chat.avatar} />
      <div className="vg-row-content">
        <div className="vg-row-title">
          <div className="vg-row-title-text">{chat.name}</div>
          <div className="vg-row-meta">{chat.time}</div>
        </div>
        <div className="vg-row-sub">
          <div className="vg-row-sub-text">
            {chat.draft ? <span style={{ color: 'var(--vg-danger)' }}>draft: </span> : null}
            {chat.preview}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            {chat.muted && <Glyph name="mute2" size={14} color="var(--vg-fg-3)" />}
            {chat.pinned && <Glyph name="pinFilled" size={14} color="var(--vg-fg-3)" fill="var(--vg-fg-3)" />}
            {chat.unread > 0 && <span className="vg-badge">{chat.unread}</span>}
          </div>
        </div>
      </div>
    </Tilt>
  );
}

// Toggle
function Toggle({ value, onChange }) {
  return (
    <button className={`vg-toggle${value ? ' on' : ''}`} onClick={() => onChange(!value)} aria-pressed={value}>
      <span className="vg-toggle-thumb" />
    </button>
  );
}

// Settings row
function SettingsRow({ glyph, label, sub, value, chevron = true, danger, onClick, right }) {
  return (
    <Tilt className="vg-srow" onClick={onClick}>
      {glyph && <div className="vg-srow-glyph"><Glyph name={glyph} size={18} color={danger ? 'var(--vg-danger)' : 'currentColor'} /></div>}
      <div className="vg-srow-content">
        <div className="vg-srow-label" style={{ color: danger ? 'var(--vg-danger)' : 'var(--vg-fg)' }}>{label}</div>
        {sub && <div className="vg-srow-sub">{sub}</div>}
      </div>
      {right}
      {value != null && <div className="vg-srow-sub" style={{ marginRight: 4 }}>{value}</div>}
      {chevron && !right && <Glyph name="chevron" size={14} color="var(--vg-fg-3)" />}
    </Tilt>
  );
}

// Progress dots
function ProgressDots() {
  return (
    <div className="vg-progress-dots">
      <span /><span /><span /><span /><span />
    </div>
  );
}

// Page transition wrapper — turnstile rotateY for forward, slide for back
function PageTransition({ children, transitionKey, direction = 'forward' }) {
  return (
    <div key={transitionKey}
      style={{
        position: 'absolute', inset: 0,
        animation: direction === 'forward'
          ? 'vg-turnstile-in 380ms cubic-bezier(.2,.7,.3,1)'
          : 'vg-slide-back-in 280ms cubic-bezier(.2,.7,.3,1)',
        transformOrigin: direction === 'forward' ? 'left center' : 'center'
      }}>
      {children}
    </div>
  );
}

// Sample data — fictional cast
const SAMPLE_CHATS = [
  { id: 'c1', name: 'Mira Sato', preview: 'see you at 7 — bring the cable?', time: '9:32', unread: 2, pinned: true },
  { id: 'c2', name: 'design crit', preview: 'Holt: pushed the v3 frames, take a look', time: '9:21', unread: 5, group: true },
  { id: 'c3', name: 'mom', preview: 'love you ❤', time: '8:14', unread: 0, muted: false },
  { id: 'c4', name: 'Vianet announcements', preview: 'Service window tonight 23:00–02:00', time: 'tue', unread: 0, channel: true, muted: true },
  { id: 'c5', name: 'Theo Park', preview: '✓✓ that\'s perfect, thanks', time: 'tue', unread: 0 },
  { id: 'c6', name: 'fri night plans', preview: 'Jules: rooftop or basement?', time: 'tue', unread: 0, group: true },
  { id: 'c7', name: 'Anya Volkov', preview: 'Photo', time: 'mon', unread: 0 },
  { id: 'c8', name: 'lab notebook', preview: 'me: draft saved', time: 'mon', unread: 0, draft: true },
  { id: 'c9', name: 'Dr. Okafor', preview: 'follow-up scheduled for next week', time: 'sun', unread: 0 },
  { id: 'c10', name: 'transit alerts', preview: 'Line 4 delayed at Pioneer', time: 'sun', unread: 0, channel: true, muted: true },
  { id: 'c11', name: 'Sami Reza', preview: 'voice message · 0:42', time: 'sat', unread: 0 },
  { id: 'c12', name: 'apartment 4B', preview: 'Kit: trash day moved to thu', time: 'fri', unread: 0, group: true },
];

const SAMPLE_CONVO = [
  { id: 'm1', from: 'them', text: 'hey — still on for tonight?', time: '9:14' },
  { id: 'm2', from: 'me',   text: 'yes! 7pm at the rooftop', time: '9:18', read: true },
  { id: 'm3', from: 'them', text: 'perfect', time: '9:18' },
  { id: 'm4', from: 'them', text: 'oh, can you bring the usb-c cable? mine\'s frayed', time: '9:30' },
  { id: 'm5', from: 'me',   text: 'on it', time: '9:31', read: true },
  { id: 'm6', from: 'them', text: 'see you at 7 — bring the cable?', time: '9:32', reply: { name: 'me', text: 'on it' } },
];

const GROUP_CONVO = [
  { id: 'g1', from: 'them', sender: 'Holt Mendez', text: 'pushed the v3 frames, take a look when you can', time: '9:18' },
  { id: 'g2', from: 'them', sender: 'Mira Sato', text: 'love the new pivot rhythm. spacing on row 3 feels tight tho', time: '9:20' },
  { id: 'g3', from: 'me', text: 'agreed — bumping to 14px gap', time: '9:21', read: true },
  { id: 'g4', from: 'them', sender: 'Holt Mendez', text: '@you can you share the swatch file too', time: '9:21' }
];

const CHANNEL_POSTS = [
  { id: 'p1', text: 'Service window scheduled tonight 23:00 — 02:00 UTC. Expect intermittent connectivity on legacy clients.', time: '9:18', views: '4.2K', reactions: [{ e: '👍', n: 142 }, { e: '🙏', n: 38 }] },
  { id: 'p2', text: 'New build: 3.4.1\n• tilt response tuned\n• fixed pivot drag on hd devices\n• 2 crash fixes', time: '8:02', views: '3.8K', reactions: [{ e: '🎉', n: 91 }] }
];

window.VG = {
  Avatar, StatusBar, TitleBar, Pivot, AppBar, Tilt, ChatRow, Toggle,
  SettingsRow, ProgressDots, PageTransition,
  colorForName, initials,
  SAMPLE_CHATS, SAMPLE_CONVO, GROUP_CONVO, CHANNEL_POSTS
};
