// Vianigram — Media screens (4)

const { useState: useStateM } = React;

function MediaPhotoScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="15 media photo" style={{ background: '#000' }}>
      <VG.StatusBar />
      <div style={{ flex: 1, position: 'relative', overflow: 'hidden' }}>
        {/* Top bar — fades after 2s in real app */}
        <div style={{ position: 'absolute', top: 0, left: 0, right: 0, padding: '12px 16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: 'linear-gradient(rgba(0,0,0,0.6), transparent)', zIndex: 2 }}>
          <div>
            <div style={{ font: 'var(--t-row-title)', color: '#fff' }}>Mira Sato</div>
            <div style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.7)' }}>today, 9:32</div>
          </div>
          <button className="vg-topbar-back" onClick={onBack} style={{ color: '#fff', fontSize: 28 }}>×</button>
        </div>
        {/* Photo placeholder — diagonal stripes */}
        <div style={{
          width: '100%', height: '100%',
          background: 'repeating-linear-gradient(135deg, #1a1a1a 0 18px, #222 18px 36px)',
          display: 'flex', alignItems: 'center', justifyContent: 'center'
        }}>
          <div style={{ font: '300 13px/1 var(--vg-mono)', color: 'rgba(255,255,255,0.35)', letterSpacing: '0.1em' }}>[ photo · 1080×1440 ]</div>
        </div>
        {/* Bottom — caption + actions */}
        <div style={{ position: 'absolute', bottom: 0, left: 0, right: 0, padding: '20px 16px', background: 'linear-gradient(transparent, rgba(0,0,0,0.85))', zIndex: 2 }}>
          <div style={{ font: 'var(--t-body)', color: '#fff', marginBottom: 16 }}>the cable in question</div>
          <div style={{ display: 'flex', justifyContent: 'space-between', font: 'var(--t-caption)', color: 'rgba(255,255,255,0.85)', textTransform: 'lowercase' }}>
            <span>save</span><span>forward</span><span>share</span><span style={{ color: '#E51400' }}>delete</span>
          </div>
        </div>
      </div>
    </div>
  );
}

function MediaVideoScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="16 media video" style={{ background: '#000' }}>
      <VG.StatusBar />
      <div style={{ flex: 1, position: 'relative', overflow: 'hidden' }}>
        <div style={{ position: 'absolute', top: 0, left: 0, right: 0, padding: '12px 16px', display: 'flex', justifyContent: 'space-between', zIndex: 2 }}>
          <div>
            <div style={{ font: 'var(--t-row-title)', color: '#fff' }}>Theo Park</div>
            <div style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.7)' }}>tue, 14:08</div>
          </div>
          <button onClick={onBack} style={{ color: '#fff', fontSize: 28, background: 'transparent', border: 0 }}>×</button>
        </div>
        <div style={{
          width: '100%', height: '100%',
          background: 'repeating-linear-gradient(45deg, #0e0e0e 0 24px, #181818 24px 48px)',
          display: 'flex', alignItems: 'center', justifyContent: 'center'
        }}>
          <div style={{ width: 64, height: 64, border: '2px solid #fff', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="play" size={28} color="#fff" fill="#fff" />
          </div>
        </div>
        <div style={{ position: 'absolute', bottom: 0, left: 0, right: 0, padding: '16px', background: 'rgba(0,0,0,0.7)', zIndex: 2 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
            <span style={{ font: 'var(--t-caption)', color: '#fff' }}>0:24</span>
            <div style={{ flex: 1, height: 2, background: 'rgba(255,255,255,0.3)', position: 'relative' }}>
              <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: '34%', background: 'var(--vg-accent)' }} />
              <div style={{ position: 'absolute', left: '34%', top: -3, width: 8, height: 8, borderRadius: 4, background: 'var(--vg-accent)' }} />
            </div>
            <span style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.7)' }}>1:08</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', font: 'var(--t-caption)', color: '#fff', textTransform: 'lowercase' }}>
            <span>save</span><span>forward</span><span>share</span>
          </div>
        </div>
      </div>
    </div>
  );
}

function VoicePlaybackScreen({ onBack }) {
  // Standalone view — bubble showcased on chat-style background
  const bars = Array.from({ length: 38 }, (_, i) => 6 + Math.abs(Math.sin(i * 1.3) * 18) + Math.cos(i * 0.7) * 4);
  return (
    <div className="vg-phone" data-screen-label="17 voice playback">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content">
          <VG.Avatar name="Sami Reza" size={40} />
          <div><div className="vg-topbar-title">Sami Reza</div><div className="vg-topbar-sub">last seen recently</div></div>
        </div>
      </div>
      <div style={{ flex: 1, padding: '12px 0' }}>
        <div className="vg-date-sep">today</div>
        <div className="vg-msg-row in">
          <div className="vg-bubble in" style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '10px 12px', minWidth: 240 }}>
            <div style={{ width: 36, height: 36, borderRadius: 18, background: 'var(--vg-accent)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
              <Glyph name="play" size={14} color="#fff" fill="#fff" />
            </div>
            <div style={{ flex: 1 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 1, height: 28 }}>
                {bars.map((h, i) => (
                  <div key={i} style={{
                    width: 2, height: h,
                    background: i < 14 ? 'var(--vg-accent)' : 'var(--vg-fg-3)'
                  }} />
                ))}
              </div>
              <div style={{ font: 'var(--t-meta)', color: 'var(--vg-fg-2)', marginTop: 2 }}>0:18 / 0:42</div>
            </div>
          </div>
        </div>
      </div>
      <div className="vg-compose">
        <button className="vg-compose-btn"><Glyph name="emoji" size={18} /></button>
        <input className="vg-compose-field" placeholder="message" />
        <button className="vg-compose-btn"><Glyph name="mic" size={18} /></button>
      </div>
    </div>
  );
}

function StickerPickerScreen({ onBack }) {
  const [tab, setTab] = useStateM('stickers');
  const sections = [{ id: 'recent', label: 'recent' }, { id: 'stickers', label: 'stickers' }, { id: 'gifs', label: 'gifs' }];
  const tints = ['#00ABA9', '#7E3878', '#E51400', '#A4C400', '#F09609', '#1BA1E2', '#60A917', '#AA00FF', '#00ABA9', '#7E3878', '#E51400', '#A4C400'];
  return (
    <div className="vg-phone" data-screen-label="18 sticker picker">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content">
          <VG.Avatar name="Mira Sato" size={40} />
          <div><div className="vg-topbar-title">Mira Sato</div><div className="vg-topbar-sub">online</div></div>
        </div>
      </div>
      <div style={{ flex: 1, background: 'var(--vg-surface-2)' }}>
        <div style={{ padding: '8px 12px' }}>
          <input className="vg-field" placeholder="search stickers" style={{ background: 'var(--vg-surface)' }} />
        </div>
        <VG.Pivot sections={sections} active={tab} onChange={setTab}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 4, padding: '8px 8px' }}>
            {tints.map((t, i) => (
              <Tilt key={i} style={{
                aspectRatio: '1', background: 'var(--vg-surface)',
                display: 'flex', alignItems: 'center', justifyContent: 'center'
              }}>
                <div style={{ width: 36, height: 36, background: t, transform: i % 3 === 0 ? 'rotate(45deg)' : i % 3 === 1 ? 'none' : 'skewX(-15deg)', borderRadius: i % 3 === 1 ? '50%' : 0 }} />
              </Tilt>
            ))}
          </div>
        </VG.Pivot>
      </div>
      <div className="vg-compose">
        <button className="vg-compose-btn"><Glyph name="emoji" size={18} color="var(--vg-accent)" /></button>
        <input className="vg-compose-field" placeholder="message" />
        <button className="vg-compose-btn"><Glyph name="attach" size={18} /></button>
      </div>
    </div>
  );
}

window.MediaScreens = { MediaPhotoScreen, MediaVideoScreen, VoicePlaybackScreen, StickerPickerScreen };
