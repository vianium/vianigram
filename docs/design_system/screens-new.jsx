// Vianigram — Stories, Calls list, Contacts, Story viewer, Camera (5 new screens)

const { useState: useStateN } = React;

const STORY_USERS = [
  { id: 's1', name: 'You', isMe: true, count: 0 },
  { id: 's2', name: 'Mira Sato', unread: 3 },
  { id: 's3', name: 'Holt Mendez', unread: 1 },
  { id: 's4', name: 'Anya Volkov', unread: 2 },
  { id: 's5', name: 'Theo Park', read: true },
  { id: 's6', name: 'Kit Tanaka', unread: 1 },
  { id: 's7', name: 'Jules Park', read: true },
  { id: 's8', name: 'Sami Reza', unread: 1 },
  { id: 's9', name: 'Bea Lin', read: true },
  { id: 's10', name: 'Yuki Mori', unread: 4 },
];

function StoryCircle({ user, size = 64 }) {
  const ring = user.isMe ? null : (user.unread ? 'unread' : 'read');
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, width: size + 16 }}>
      <div className={`vg-story-ring ${ring || ''}`} style={{ width: size + 4, height: size + 4 }}>
        <div style={{ position: 'relative' }}>
          <VG.Avatar name={user.name} size={size - 4} />
          {user.isMe && (
            <div style={{
              position: 'absolute', bottom: -2, right: -2, width: 22, height: 22,
              borderRadius: 11, background: 'var(--vg-accent)', border: '2px solid var(--vg-bg)',
              display: 'flex', alignItems: 'center', justifyContent: 'center'
            }}>
              <Glyph name="plus" size={12} color="#fff" />
            </div>
          )}
        </div>
      </div>
      <div style={{ font: 'var(--t-meta)', color: 'var(--vg-fg-2)', maxWidth: size + 16, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', textAlign: 'center' }}>
        {user.isMe ? 'your story' : user.name.split(' ')[0]}
      </div>
    </div>
  );
}

function StoriesScreen({ onBack, onOpenStory }) {
  return (
    <div className="vg-phone" data-screen-label="29 stories">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="stories" />
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {/* Add to story large card */}
        <Tilt style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 24px', borderBottom: '1px solid var(--vg-divider)' }}>
          <div style={{ position: 'relative' }}>
            <VG.Avatar name="You Y" size={56} />
            <div style={{
              position: 'absolute', bottom: -2, right: -2, width: 22, height: 22,
              borderRadius: 11, background: 'var(--vg-accent)', border: '2px solid var(--vg-bg)',
              display: 'flex', alignItems: 'center', justifyContent: 'center'
            }}>
              <Glyph name="plus" size={12} color="#fff" />
            </div>
          </div>
          <div style={{ flex: 1 }}>
            <div style={{ font: 'var(--t-row-title)' }}>add to your story</div>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>share a moment with friends</div>
          </div>
          <Glyph name="camera" size={20} color="var(--vg-accent)" />
        </Tilt>

        <div style={{ padding: '16px 24px 8px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>recent · 6</div>
        {STORY_USERS.slice(1, 7).map(u => (
          <Tilt key={u.id} className="vg-row" style={{ minHeight: 72 }} onClick={() => onOpenStory?.(u)}>
            <div className={`vg-story-ring ${u.unread ? 'unread' : 'read'}`} style={{ width: 56, height: 56 }}>
              <VG.Avatar name={u.name} size={48} />
            </div>
            <div className="vg-row-content">
              <div style={{ font: 'var(--t-row-title)' }}>{u.name}</div>
              <div style={{ font: 'var(--t-caption)', color: u.unread ? 'var(--vg-accent)' : 'var(--vg-fg-2)' }}>
                {u.unread ? `${u.unread} new · ${Math.floor(Math.random() * 6) + 1}h ago` : `viewed · ${Math.floor(Math.random() * 20) + 4}h ago`}
              </div>
            </div>
            <Glyph name="chevron" size={14} color="var(--vg-fg-3)" />
          </Tilt>
        ))}

        <div style={{ padding: '20px 24px 8px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>viewed · 4</div>
        {STORY_USERS.slice(7).map(u => (
          <Tilt key={u.id} className="vg-row" style={{ minHeight: 64 }}>
            <div className="vg-story-ring read" style={{ width: 48, height: 48 }}>
              <VG.Avatar name={u.name} size={40} />
            </div>
            <div className="vg-row-content">
              <div style={{ font: 'var(--t-row-title)' }}>{u.name}</div>
              <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>viewed · yesterday</div>
            </div>
          </Tilt>
        ))}
      </div>
      <VG.AppBar buttons={[
        { glyph: 'camera', label: 'capture' },
        { glyph: 'search', label: 'search' }
      ]} onMore={() => {}} />
    </div>
  );
}

function StoryViewerScreen({ onBack }) {
  const segs = 4, current = 1;
  return (
    <div className="vg-phone" data-screen-label="30 story viewer" style={{ background: '#000' }}>
      <VG.StatusBar />
      {/* Segment progress */}
      <div style={{ display: 'flex', gap: 4, padding: '4px 12px 8px' }}>
        {Array.from({ length: segs }).map((_, i) => (
          <div key={i} style={{ flex: 1, height: 2, background: 'rgba(255,255,255,0.2)', overflow: 'hidden' }}>
            {i < current && <div style={{ width: '100%', height: '100%', background: '#fff' }} />}
            {i === current && <div style={{ width: '62%', height: '100%', background: '#fff', animation: 'vg-page-in 200ms' }} />}
          </div>
        ))}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '4px 16px 12px' }}>
        <VG.Avatar name="Mira Sato" size={32} />
        <div style={{ flex: 1, color: '#fff' }}>
          <div style={{ font: '600 13px/16px var(--vg-font)' }}>Mira Sato</div>
          <div style={{ font: 'var(--t-meta)', opacity: 0.7 }}>4h ago · kyoto</div>
        </div>
        <button onClick={onBack} style={{ background: 'transparent', border: 0, color: '#fff', fontSize: 24 }}>×</button>
      </div>
      <div style={{ flex: 1, position: 'relative', background: 'repeating-linear-gradient(45deg, #1a1a1a 0 22px, #232323 22px 44px)', overflow: 'hidden' }}>
        <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', font: '300 13px/1 var(--vg-mono)', color: 'rgba(255,255,255,0.35)', letterSpacing: '0.1em' }}>
          [ story · photo ]
        </div>
        <div style={{ position: 'absolute', bottom: 24, left: 16, right: 16, color: '#fff', font: '300 18px/24px var(--vg-font)' }}>
          finally found the right cable shop. five floors, no elevator.
        </div>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 12px', background: '#000', borderTop: '1px solid rgba(255,255,255,0.1)' }}>
        <input style={{ flex: 1, background: 'rgba(255,255,255,0.08)', border: '1px solid rgba(255,255,255,0.2)', color: '#fff', padding: '10px 14px', font: 'var(--t-body)', outline: 'none' }} placeholder="reply privately…" />
        <button style={{ width: 36, height: 36, background: 'transparent', border: 0, color: '#fff' }}><Glyph name="reaction" size={20} color="#fff" /></button>
        <button style={{ width: 36, height: 36, background: 'transparent', border: 0, color: '#fff' }}><Glyph name="send" size={18} color="#fff" fill="#fff" /></button>
      </div>
    </div>
  );
}

const CALL_LOG = [
  { id: 'cl1', name: 'Mira Sato', when: 'today, 9:32', dir: 'out', kind: 'video', dur: '18:42' },
  { id: 'cl2', name: 'Theo Park', when: 'today, 8:14', dir: 'in', kind: 'audio', dur: '4:08' },
  { id: 'cl3', name: 'Anya Volkov', when: 'tue, 22:08', dir: 'missed', kind: 'audio', dur: null },
  { id: 'cl4', name: 'Holt Mendez', when: 'tue, 18:30', dir: 'out', kind: 'audio', dur: '0:42' },
  { id: 'cl5', name: 'mom', when: 'mon, 19:22', dir: 'in', kind: 'video', dur: '52:13' },
  { id: 'cl6', name: 'Dr. Okafor', when: 'sun, 14:00', dir: 'out', kind: 'audio', dur: '12:04' },
  { id: 'cl7', name: 'Sami Reza', when: 'sat, 23:11', dir: 'missed', kind: 'audio', dur: null },
  { id: 'cl8', name: 'Kit Tanaka', when: 'fri, 17:45', dir: 'out', kind: 'audio', dur: '2:30' },
];

function CallsListScreen({ onBack }) {
  const [tab, setTab] = useStateN('all');
  const sections = [{ id: 'all', label: 'all' }, { id: 'missed', label: 'missed' }];
  const items = tab === 'all' ? CALL_LOG : CALL_LOG.filter(c => c.dir === 'missed');
  const arrowFor = (dir) => {
    if (dir === 'out') return { glyph: '↗', color: 'var(--vg-success)' };
    if (dir === 'in') return { glyph: '↙', color: 'var(--vg-accent)' };
    return { glyph: '↙', color: 'var(--vg-danger)' };
  };
  return (
    <div className="vg-phone" data-screen-label="31 calls list">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="calls" />
      <VG.Pivot sections={sections} active={tab} onChange={setTab}>
        {items.map(c => {
          const a = arrowFor(c.dir);
          return (
            <Tilt key={c.id} className="vg-row" style={{ minHeight: 68 }}>
              <VG.Avatar name={c.name} size={48} />
              <div className="vg-row-content">
                <div style={{ font: 'var(--t-row-title)', color: c.dir === 'missed' ? 'var(--vg-danger)' : 'var(--vg-fg)' }}>{c.name}</div>
                <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)', display: 'flex', alignItems: 'center', gap: 6 }}>
                  <span style={{ color: a.color, fontWeight: 600 }}>{a.glyph}</span>
                  <span>{c.dir === 'missed' ? 'missed' : c.dir === 'out' ? 'outgoing' : 'incoming'}</span>
                  <span>·</span>
                  <span>{c.when}</span>
                  {c.dur && <><span>·</span><span>{c.dur}</span></>}
                </div>
              </div>
              <button style={{ background: 'transparent', border: 0, color: 'var(--vg-accent)', padding: 8 }}>
                <Glyph name={c.kind === 'video' ? 'video' : 'phone'} size={20} color="var(--vg-accent)" />
              </button>
            </Tilt>
          );
        })}
      </VG.Pivot>
      <VG.AppBar buttons={[
        { glyph: 'plus', label: 'new call' },
        { glyph: 'search', label: 'search' }
      ]} onMore={() => {}} />
    </div>
  );
}

const CONTACTS_FULL = [
  { id: 'k1', name: 'Anya Volkov', sub: 'last seen 12 min ago', online: false },
  { id: 'k2', name: 'Bea Lin', sub: 'online', online: true },
  { id: 'k3', name: 'Cyrus Vance', sub: 'last seen recently', online: false },
  { id: 'k4', name: 'Dani Okafor', sub: 'online', online: true },
  { id: 'k5', name: 'Elif Demir', sub: 'last seen yesterday', online: false },
  { id: 'k6', name: 'Holt Mendez', sub: 'online', online: true },
  { id: 'k7', name: 'Jules Park', sub: 'last seen 2 hours ago', online: false },
  { id: 'k8', name: 'Kit Tanaka', sub: 'last seen recently', online: false },
  { id: 'k9', name: 'Mira Sato', sub: 'last seen 2 hours ago', online: false },
  { id: 'k10', name: 'Sami Reza', sub: 'last seen yesterday', online: false },
  { id: 'k11', name: 'Theo Park', sub: 'online', online: true },
  { id: 'k12', name: 'Yuki Mori', sub: 'last seen recently', online: false },
];

function ContactsScreen({ onBack }) {
  const groups = {};
  CONTACTS_FULL.forEach(c => { const k = c.name[0].toUpperCase(); (groups[k] ||= []).push(c); });
  const onlineCount = CONTACTS_FULL.filter(c => c.online).length;
  return (
    <div className="vg-phone" data-screen-label="32 contacts">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="contacts" />
      <div style={{ padding: '8px 24px 12px', font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>
        {CONTACTS_FULL.length} contacts · <span style={{ color: 'var(--vg-success)' }}>{onlineCount} online</span>
      </div>
      <div style={{ padding: '0 12px 8px' }}>
        <input className="vg-field" placeholder="search contacts" style={{ background: 'var(--vg-surface)' }} />
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        <Tilt className="vg-row" style={{ minHeight: 56 }}>
          <div className="vg-avatar" style={{ background: 'var(--vg-accent)', width: 44, height: 44 }}><Glyph name="plus" size={20} color="#fff" /></div>
          <div className="vg-row-content"><div style={{ font: 'var(--t-row-title)', color: 'var(--vg-accent)' }}>add new contact</div></div>
        </Tilt>
        <Tilt className="vg-row" style={{ minHeight: 56 }}>
          <div className="vg-avatar" style={{ background: 'var(--vg-accent)', width: 44, height: 44 }}><Glyph name="qr" size={20} color="#fff" /></div>
          <div className="vg-row-content"><div style={{ font: 'var(--t-row-title)', color: 'var(--vg-accent)' }}>invite via qr</div></div>
        </Tilt>
        {Object.keys(groups).sort().map(letter => (
          <div key={letter}>
            <div style={{ padding: '14px 24px 4px', font: '300 22px/1 var(--vg-font)', color: 'var(--vg-fg-3)' }}>{letter}</div>
            {groups[letter].map(c => (
              <Tilt key={c.id} className="vg-row" style={{ minHeight: 60 }}>
                <div style={{ position: 'relative' }}>
                  <VG.Avatar name={c.name} size={44} />
                  {c.online && (
                    <div style={{ position: 'absolute', bottom: 0, right: 0, width: 12, height: 12, borderRadius: 6, background: 'var(--vg-success)', border: '2px solid var(--vg-bg)' }} />
                  )}
                </div>
                <div className="vg-row-content">
                  <div style={{ font: 'var(--t-row-title)' }}>{c.name}</div>
                  <div style={{ font: 'var(--t-caption)', color: c.online ? 'var(--vg-success)' : 'var(--vg-fg-2)' }}>{c.sub}</div>
                </div>
              </Tilt>
            ))}
          </div>
        ))}
      </div>
      <VG.AppBar buttons={[
        { glyph: 'plus', label: 'add' },
        { glyph: 'qr', label: 'invite' }
      ]} onMore={() => {}} />
    </div>
  );
}

function StoryCameraScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="33 story camera" style={{ background: '#000' }}>
      <VG.StatusBar />
      <div style={{ flex: 1, position: 'relative', overflow: 'hidden', background: 'radial-gradient(circle at 50% 40%, #2a2a2a, #000)' }}>
        {/* Top controls */}
        <div style={{ position: 'absolute', top: 8, left: 12, right: 12, display: 'flex', justifyContent: 'space-between', zIndex: 2 }}>
          <button onClick={onBack} style={{ background: 'transparent', border: 0, color: '#fff', fontSize: 24 }}>×</button>
          <div style={{ display: 'flex', gap: 16 }}>
            <Glyph name="sun" size={20} color="#fff" />
            <Glyph name="paint" size={20} color="#fff" />
          </div>
        </div>
        {/* Faux viewfinder grid */}
        <div style={{ position: 'absolute', inset: '20% 10%', border: '1px dashed rgba(255,255,255,0.15)' }}>
          <div style={{ position: 'absolute', top: '33%', left: 0, right: 0, height: 1, background: 'rgba(255,255,255,0.1)' }} />
          <div style={{ position: 'absolute', top: '66%', left: 0, right: 0, height: 1, background: 'rgba(255,255,255,0.1)' }} />
          <div style={{ position: 'absolute', left: '33%', top: 0, bottom: 0, width: 1, background: 'rgba(255,255,255,0.1)' }} />
          <div style={{ position: 'absolute', left: '66%', top: 0, bottom: 0, width: 1, background: 'rgba(255,255,255,0.1)' }} />
        </div>
        {/* Mode pills */}
        <div style={{ position: 'absolute', bottom: 120, left: 0, right: 0, display: 'flex', justifyContent: 'center', gap: 18, zIndex: 2 }}>
          {['story', 'photo', 'video', 'round'].map((m, i) => (
            <div key={m} style={{
              font: 'var(--t-caption)', color: i === 0 ? 'var(--vg-accent)' : 'rgba(255,255,255,0.5)',
              textTransform: 'uppercase', letterSpacing: '0.1em',
              borderBottom: i === 0 ? '1px solid var(--vg-accent)' : 'none',
              paddingBottom: 4
            }}>{m}</div>
          ))}
        </div>
        {/* Capture controls */}
        <div style={{ position: 'absolute', bottom: 24, left: 0, right: 0, display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0 32px', zIndex: 2 }}>
          <div style={{ width: 44, height: 44, background: 'rgba(255,255,255,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="image" size={20} color="#fff" />
          </div>
          <button style={{ width: 76, height: 76, borderRadius: 38, border: '4px solid #fff', background: 'transparent', position: 'relative' }}>
            <div style={{ position: 'absolute', inset: 6, borderRadius: '50%', background: '#fff' }} />
          </button>
          <div style={{ width: 44, height: 44, background: 'rgba(255,255,255,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="refresh" size={20} color="#fff" />
          </div>
        </div>
        {/* Privacy chip */}
        <div style={{ position: 'absolute', top: 56, left: '50%', transform: 'translateX(-50%)', padding: '4px 12px', background: 'rgba(0,0,0,0.5)', font: 'var(--t-caption)', color: '#fff', display: 'flex', alignItems: 'center', gap: 6 }}>
          <Glyph name="users" size={12} color="#fff" />
          <span>everyone · 24h</span>
        </div>
      </div>
    </div>
  );
}

window.NewScreens = { StoriesScreen, StoryViewerScreen, CallsListScreen, ContactsScreen, StoryCameraScreen };
