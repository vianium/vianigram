// Vianigram — Compose / picker flows (5)

const { useState: useStateC } = React;

const CONTACTS = [
  { id: 'u1', name: 'Anya Volkov' }, { id: 'u2', name: 'Bea Lin' },
  { id: 'u3', name: 'Cyrus Vance' }, { id: 'u4', name: 'Dani Okafor' },
  { id: 'u5', name: 'Elif Demir' }, { id: 'u6', name: 'Holt Mendez' },
  { id: 'u7', name: 'Jules Park' }, { id: 'u8', name: 'Kit Tanaka' },
  { id: 'u9', name: 'Mira Sato' }, { id: 'u10', name: 'Sami Reza' },
  { id: 'u11', name: 'Theo Park' }, { id: 'u12', name: 'Yuki Mori' },
];

function ContactPickerScreen({ onBack, multi = false, title = 'new chat', stepLabel }) {
  const [sel, setSel] = useStateC(new Set());
  const groups = {};
  CONTACTS.forEach(c => { const k = c.name[0].toUpperCase(); (groups[k] ||= []).push(c); });

  return (
    <div className="vg-phone" data-screen-label="10 contact picker">
      <VG.StatusBar />
      <VG.TitleBar pageTitle={title} />
      {stepLabel && <div style={{ padding: '0 24px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>{stepLabel}</div>}
      <div style={{ padding: '12px 12px 0' }}>
        <input className="vg-field" placeholder="search contacts" style={{ background: 'var(--vg-surface)' }} />
      </div>
      <div style={{ flex: 1, overflowY: 'auto', padding: '8px 0' }}>
        {!multi && (
          <>
            <Tilt className="vg-row" style={{ minHeight: 60 }}>
              <div className="vg-avatar" style={{ background: 'var(--vg-accent)' }}><Glyph name="users" size={22} color="#fff" /></div>
              <div className="vg-row-content"><div className="vg-row-title-text" style={{ font: 'var(--t-row-title)' }}>new group</div></div>
            </Tilt>
            <Tilt className="vg-row" style={{ minHeight: 60 }}>
              <div className="vg-avatar" style={{ background: 'var(--vg-accent)' }}><Glyph name="channel" size={22} color="#fff" /></div>
              <div className="vg-row-content"><div className="vg-row-title-text" style={{ font: 'var(--t-row-title)' }}>new channel</div></div>
            </Tilt>
            <Tilt className="vg-row" style={{ minHeight: 60 }}>
              <div className="vg-avatar" style={{ background: 'var(--vg-accent)' }}><Glyph name="lock" size={22} color="#fff" /></div>
              <div className="vg-row-content"><div className="vg-row-title-text" style={{ font: 'var(--t-row-title)' }}>new secret chat</div></div>
            </Tilt>
          </>
        )}
        {Object.keys(groups).sort().map(letter => (
          <div key={letter}>
            <div style={{ padding: '12px 24px 4px', font: '300 22px/1 var(--vg-font)', color: 'var(--vg-fg-3)' }}>{letter}</div>
            {groups[letter].map(c => (
              <Tilt key={c.id} className="vg-row" style={{ minHeight: 64 }} onClick={() => {
                if (multi) {
                  const ns = new Set(sel);
                  ns.has(c.id) ? ns.delete(c.id) : ns.add(c.id);
                  setSel(ns);
                }
              }}>
                <VG.Avatar name={c.name} size={44} />
                <div className="vg-row-content">
                  <div className="vg-row-title-text" style={{ font: 'var(--t-row-title)' }}>{c.name}</div>
                  <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>last seen recently</div>
                </div>
                {multi && (
                  <div style={{
                    width: 22, height: 22, borderRadius: 11, border: '2px solid var(--vg-fg-3)',
                    background: sel.has(c.id) ? 'var(--vg-accent)' : 'transparent',
                    borderColor: sel.has(c.id) ? 'var(--vg-accent)' : 'var(--vg-fg-3)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center'
                  }}>
                    {sel.has(c.id) && <Glyph name="check" size={12} color="#fff" />}
                  </div>
                )}
              </Tilt>
            ))}
          </div>
        ))}
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        ...(multi ? [{ glyph: 'forward', label: 'next' }] : [])
      ]} onMore={() => {}} />
    </div>
  );
}

function NewGroupMetaScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="12 new group meta">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="new group" />
      <div style={{ padding: '24px', flex: 1 }}>
        <div style={{ display: 'flex', gap: 16, alignItems: 'center', marginBottom: 28 }}>
          <div style={{ width: 96, height: 96, borderRadius: 48, background: 'var(--vg-surface)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="camera" size={28} color="var(--vg-fg-2)" />
          </div>
          <div style={{ flex: 1, font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>tap to set group photo</div>
        </div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em', marginBottom: 4 }}>group name</div>
        <input className="vg-field" defaultValue="design crit" style={{ background: 'transparent', borderTop: 0, borderLeft: 0, borderRight: 0, borderBottom: '2px solid var(--vg-fg-3)', padding: '8px 0', fontSize: 18 }} />
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', marginTop: 12 }}>5 members selected</div>
        <div style={{ display: 'flex', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
          {CONTACTS.slice(0, 5).map(c => <VG.Avatar key={c.id} name={c.name} size={36} />)}
        </div>
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'check', label: 'create' }
      ]} onMore={() => {}} />
    </div>
  );
}

function NewChannelScreen({ onBack }) {
  const [pub, setPub] = useStateC(true);
  return (
    <div className="vg-phone" data-screen-label="13 new channel">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="new channel" />
      <div style={{ padding: '20px 24px', flex: 1, overflowY: 'auto' }}>
        <div style={{ display: 'flex', gap: 16, alignItems: 'center', marginBottom: 24 }}>
          <div style={{ width: 80, height: 80, borderRadius: 40, background: 'var(--vg-surface)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Glyph name="camera" size={24} color="var(--vg-fg-2)" />
          </div>
          <div style={{ flex: 1 }}>
            <input className="vg-field" placeholder="channel name" style={{ background: 'transparent', borderTop: 0, borderLeft: 0, borderRight: 0, borderBottom: '2px solid var(--vg-fg-3)', padding: '8px 0', fontSize: 18 }} />
          </div>
        </div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>description</div>
        <textarea className="vg-field" rows={3} placeholder="tell people what this channel is about" style={{ background: 'transparent', border: 0, borderBottom: '2px solid var(--vg-fg-3)', padding: '8px 0', fontSize: 15, resize: 'none', width: '100%' }} />
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 0' }}>
          <div>
            <div style={{ font: 'var(--t-row-title)' }}>public channel</div>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>anyone can find and join</div>
          </div>
          <VG.Toggle value={pub} onChange={setPub} />
        </div>
        {pub && (
          <>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>username</div>
            <div style={{ display: 'flex', gap: 4, alignItems: 'baseline', borderBottom: '2px solid var(--vg-accent)', padding: '6px 0' }}>
              <span style={{ color: 'var(--vg-fg-2)' }}>vianigram.me/</span>
              <input defaultValue="design_crit" style={{ flex: 1, background: 'transparent', border: 0, color: 'var(--vg-fg)', font: 'var(--t-body)', outline: 'none' }} />
              <Glyph name="check" size={14} color="var(--vg-success)" />
            </div>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-success)', marginTop: 4 }}>username is available</div>
          </>
        )}
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'check', label: 'create' }
      ]} onMore={() => {}} />
    </div>
  );
}

function ForwardPickerScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="14 forward picker">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="forward to" />
      <div style={{ margin: '12px 24px', borderLeft: '3px solid var(--vg-accent)', padding: '6px 12px', background: 'var(--vg-surface)' }}>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-accent)', fontWeight: 600 }}>Mira Sato</div>
        <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)' }}>see you at 7 — bring the cable?</div>
      </div>
      <div style={{ padding: '0 12px 8px' }}>
        <input className="vg-field" placeholder="search chats" style={{ background: 'var(--vg-surface)' }} />
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {VG.SAMPLE_CHATS.slice(0, 8).map(c => (
          <Tilt key={c.id} className="vg-row" style={{ minHeight: 64 }}>
            <VG.Avatar name={c.name} size={44} />
            <div className="vg-row-content"><div style={{ font: 'var(--t-row-title)' }}>{c.name}</div></div>
            <div style={{ width: 22, height: 22, borderRadius: 11, border: '2px solid var(--vg-fg-3)' }} />
          </Tilt>
        ))}
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'send', label: 'forward' }
      ]} onMore={() => {}} />
    </div>
  );
}

window.ComposeScreens = { ContactPickerScreen, NewGroupMetaScreen, NewChannelScreen, ForwardPickerScreen };
