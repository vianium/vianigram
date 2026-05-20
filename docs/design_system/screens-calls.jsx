// Vianigram — Calls + Secret chat (3)

function OutgoingCallScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="26 outgoing call" style={{ background: 'linear-gradient(180deg, var(--vg-accent) 0%, color-mix(in oklab, var(--vg-accent) 50%, #000) 100%)' }}>
      <VG.StatusBar />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '60px 24px 40px', color: '#fff' }}>
        <div style={{ font: 'var(--t-caption)', textTransform: 'uppercase', letterSpacing: '0.18em', opacity: 0.7, marginBottom: 40 }}>vianigram audio call</div>
        <div style={{ width: 160, height: 160, borderRadius: 80, overflow: 'hidden', marginBottom: 24, border: '2px solid rgba(255,255,255,0.3)' }}>
          <VG.Avatar name="Mira Sato" size={160} />
        </div>
        <div style={{ font: '300 32px/1 var(--vg-font)', letterSpacing: '-0.01em' }}>Mira Sato</div>
        <div style={{ font: 'var(--t-body)', opacity: 0.8, marginTop: 8 }}>00:42</div>
        <div style={{ marginTop: 16, opacity: 0.6 }}><ProgressDots /></div>
        <div style={{ marginTop: 'auto', display: 'flex', gap: 24, alignItems: 'center' }}>
          <button style={{ width: 64, height: 64, borderRadius: 32, background: 'rgba(255,255,255,0.15)', border: 'none', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="mute" size={22} color="#fff" />
          </button>
          <button onClick={onBack} style={{ width: 80, height: 80, borderRadius: 40, background: '#E51400', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 0 0 4px rgba(229,20,0,0.25)' }}>
            <Glyph name="endcall" size={28} color="#fff" fill="#fff" />
          </button>
          <button style={{ width: 64, height: 64, borderRadius: 32, background: 'rgba(255,255,255,0.15)', border: 'none', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="speaker" size={22} color="#fff" />
          </button>
        </div>
      </div>
    </div>
  );
}

function IncomingCallScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="27 incoming call" style={{ background: 'linear-gradient(180deg, #7E3878 0%, #2a1228 100%)' }}>
      <VG.StatusBar />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '60px 24px 40px', color: '#fff' }}>
        <div style={{ font: 'var(--t-caption)', textTransform: 'uppercase', letterSpacing: '0.18em', opacity: 0.7, marginBottom: 40 }}>incoming vianigram call</div>
        <div style={{ width: 160, height: 160, borderRadius: 80, overflow: 'hidden', marginBottom: 24, border: '2px solid rgba(255,255,255,0.3)' }}>
          <VG.Avatar name="Theo Park" size={160} />
        </div>
        <div style={{ font: '300 32px/1 var(--vg-font)' }}>Theo Park</div>
        <div style={{ font: 'var(--t-body)', opacity: 0.8, marginTop: 8 }}>ringing…</div>
        <div style={{ marginTop: 'auto', display: 'flex', gap: 60, justifyContent: 'space-between', width: '100%', padding: '0 16px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
            <button style={{ width: 80, height: 80, borderRadius: 40, background: '#E51400', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center' }} onClick={onBack}>
              <Glyph name="endcall" size={28} color="#fff" fill="#fff" />
            </button>
            <span style={{ font: 'var(--t-caption)', opacity: 0.85 }}>decline</span>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
            <button style={{ width: 80, height: 80, borderRadius: 40, background: '#60A917', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <Glyph name="phone" size={28} color="#fff" fill="#fff" />
            </button>
            <span style={{ font: 'var(--t-caption)', opacity: 0.85 }}>accept</span>
          </div>
        </div>
      </div>
    </div>
  );
}

function SecretKeyScreen({ onBack }) {
  const emojis = ['🦊','🌙','⛰','🍵','🪐','🌾','🐢','🦋','⚓','🍊','🪶','🎐','🪁','🐚','🌿','🦉'];
  const fp = 'A2F4 9C71 0E3D 8B22 1DAF 5C09 7E14 86B0';
  return (
    <div className="vg-phone" data-screen-label="28 secret key">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content"><div className="vg-topbar-title">encryption key</div><div className="vg-topbar-sub">with Anya Volkov</div></div>
        <Glyph name="lock" size={16} color="var(--vg-accent)" />
      </div>
      <div style={{ flex: 1, padding: '20px 24px', display: 'flex', flexDirection: 'column' }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12, padding: '8px', background: 'var(--vg-surface)', marginBottom: 24 }}>
          {emojis.map((e, i) => (
            <div key={i} style={{ aspectRatio: '1', display: 'flex', alignItems: 'center', justifyContent: 'center', font: '300 36px/1 var(--vg-font)' }}>{e}</div>
          ))}
        </div>
        <div style={{ font: 'var(--t-mono)', fontFamily: 'var(--vg-mono)', fontSize: 13, lineHeight: '20px', color: 'var(--vg-fg-2)', textAlign: 'center', marginBottom: 16, letterSpacing: '0.05em' }}>{fp}</div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textAlign: 'center', maxWidth: 280, alignSelf: 'center', lineHeight: '18px' }}>
          these emoji and the hex fingerprint must match those of your contact. if they do, your chat is end-to-end encrypted.
        </div>
        <div style={{ marginTop: 'auto', textAlign: 'center', font: 'var(--t-row-title)', color: 'var(--vg-accent)', padding: 12 }}>compare via qr</div>
      </div>
    </div>
  );
}

window.CallScreens = { OutgoingCallScreen, IncomingCallScreen, SecretKeyScreen };
