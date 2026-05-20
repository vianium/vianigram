// Vianigram — Profile / settings (7)

const { useState: useStateP } = React;

function SelfProfileScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="19 self profile">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="edit profile" />
      <div style={{ flex: 1, overflowY: 'auto', padding: '12px 24px 20px' }}>
        <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 24 }}>
          <div style={{ position: 'relative' }}>
            <VG.Avatar name="You Yamada" size={120} />
            <div style={{ position: 'absolute', bottom: 0, right: 0, width: 36, height: 36, borderRadius: 18, background: 'var(--vg-accent)', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '2px solid var(--vg-bg)' }}>
              <Glyph name="camera" size={16} color="#fff" />
            </div>
          </div>
        </div>
        {[
          ['first name', 'You'],
          ['last name', 'Yamada'],
          ['username', '@you_y'],
          ['phone', '+81 90 1234 5678'],
        ].map(([l, v]) => (
          <div key={l} style={{ marginBottom: 16 }}>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>{l}</div>
            <div style={{ borderBottom: '2px solid var(--vg-divider)', font: 'var(--t-row-title)', padding: '6px 0' }}>{v}</div>
          </div>
        ))}
        <div style={{ marginBottom: 16 }}>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>bio</div>
          <div style={{ borderBottom: '2px solid var(--vg-divider)', font: 'var(--t-body)', color: 'var(--vg-fg-2)', padding: '6px 0' }}>cartographer of small things. tokyo → kyoto.</div>
        </div>
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'check', label: 'save' }
      ]} onMore={() => {}} />
    </div>
  );
}

function OtherProfileScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="20 other profile">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content"><div className="vg-topbar-title">info</div></div>
        <button className="vg-compose-btn"><Glyph name="more" size={18} /></button>
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        <div style={{ padding: '8px 24px 24px', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <VG.Avatar name="Mira Sato" size={120} />
          <div style={{ font: '300 26px/30px var(--vg-font)', marginTop: 12 }}>Mira Sato</div>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)', marginTop: 2 }}>last seen 2 hours ago</div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', borderTop: '1px solid var(--vg-divider)', borderBottom: '1px solid var(--vg-divider)' }}>
          {[
            { g: 'chat', l: 'message' },
            { g: 'phone', l: 'call' },
            { g: 'video', l: 'video' },
            { g: 'mute', l: 'mute' }
          ].map((b, i) => (
            <Tilt key={i} style={{ padding: '14px 0', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, color: 'var(--vg-accent)' }}>
              <Glyph name={b.g} size={20} color="var(--vg-accent)" />
              <span style={{ font: 'var(--t-caption)', color: 'var(--vg-accent)' }}>{b.l}</span>
            </Tilt>
          ))}
        </div>
        <div style={{ padding: '12px 24px' }}>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>bio</div>
          <div style={{ font: 'var(--t-body)', padding: '6px 0' }}>field engineer. coffee snob. ↗ kyoto.</div>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em', marginTop: 16 }}>username</div>
          <div style={{ font: 'var(--t-body)', color: 'var(--vg-accent)', padding: '6px 0' }}>@mira.sato</div>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em', marginTop: 16 }}>phone</div>
          <div style={{ font: 'var(--t-body)', padding: '6px 0' }}>+81 90 8842 1003</div>
        </div>
        <VG.SettingsRow glyph="bell" label="notifications" right={<VG.Toggle value={true} onChange={() => {}} />} chevron={false} />
        <VG.SettingsRow glyph="image" label="shared media" value="142" />
        <VG.SettingsRow glyph="lock" label="encryption key" sub="this chat is end-to-end encrypted" />
        <VG.SettingsRow glyph="trash" label="block user" danger chevron={false} />
      </div>
    </div>
  );
}

function GroupInfoScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="21 group info">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content"><div className="vg-topbar-title">group info</div></div>
        <button className="vg-compose-btn"><Glyph name="edit" size={16} /></button>
      </div>
      <div style={{ flex: 1, overflowY: 'auto' }}>
        <div style={{ padding: '8px 24px 24px', textAlign: 'center' }}>
          <VG.Avatar name="design crit" size={96} />
          <div style={{ font: '300 26px/30px var(--vg-font)', marginTop: 12 }}>design crit</div>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>8 members, 3 online</div>
        </div>
        <VG.SettingsRow glyph="bell" label="notifications" right={<VG.Toggle value={true} onChange={() => {}} />} chevron={false} />
        <VG.SettingsRow glyph="image" label="shared media" value="84" />
        <VG.SettingsRow glyph="shield" label="permissions" />
        <div style={{ padding: '12px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>8 members</div>
        <Tilt className="vg-row" style={{ minHeight: 60 }}>
          <div className="vg-avatar" style={{ background: 'var(--vg-accent)', width: 44, height: 44 }}><Glyph name="plus" size={20} color="#fff" /></div>
          <div className="vg-row-content"><div style={{ font: 'var(--t-row-title)', color: 'var(--vg-accent)' }}>add member</div></div>
        </Tilt>
        {['Mira Sato', 'Holt Mendez', 'Theo Park', 'Anya Volkov', 'Kit Tanaka'].map((n, i) => (
          <Tilt key={n} className="vg-row" style={{ minHeight: 60 }}>
            <VG.Avatar name={n} size={44} />
            <div className="vg-row-content">
              <div style={{ font: 'var(--t-row-title)' }}>{n}</div>
              <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>last seen recently</div>
            </div>
            {i === 0 && <div style={{ font: 'var(--t-meta)', color: 'var(--vg-accent)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>admin</div>}
          </Tilt>
        ))}
        <VG.SettingsRow glyph="trash" label="leave group" danger chevron={false} />
      </div>
    </div>
  );
}

function SettingsRootScreen({ onBack, onOpen }) {
  const [tab, setTab] = useStateP('settings');
  const sections = [{ id: 'settings', label: 'settings' }, { id: 'about', label: 'about' }];
  return (
    <div className="vg-phone" data-screen-label="22 settings root">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="settings" />
      <VG.Pivot sections={sections} active={tab} onChange={setTab}>
        {tab === 'settings' ? (
          <>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '12px 24px 20px' }}>
              <VG.Avatar name="You Yamada" size={64} />
              <div>
                <div style={{ font: '300 22px/26px var(--vg-font)' }}>You Yamada</div>
                <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>+81 90 1234 5678 · @you_y</div>
              </div>
            </div>
            <VG.SettingsRow glyph="bell" label="notifications and sounds" onClick={() => onOpen?.('notifs')} />
            <VG.SettingsRow glyph="lock" label="privacy and security" onClick={() => onOpen?.('privacy')} />
            <VG.SettingsRow glyph="database" label="data and storage" />
            <VG.SettingsRow glyph="chat" label="chat settings" />
            <VG.SettingsRow glyph="sticker" label="stickers" />
            <VG.SettingsRow glyph="device" label="devices" sub="3 active" />
            <VG.SettingsRow glyph="globe" label="language" value="english" />
          </>
        ) : (
          <div style={{ padding: '40px 24px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 20 }}>
              <div style={{ width: 36, height: 36, background: 'var(--vg-accent)', transform: 'rotate(45deg)' }} />
              <div style={{ font: '300 32px/1 var(--vg-font)' }}>vianigram</div>
            </div>
            <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)', marginBottom: 8 }}>version 3.4.1 (build 21088)</div>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', maxWidth: 280, marginTop: 24 }}>
              fast, secure messaging for windows phone. open source under gpl-2.0.
            </div>
            <div style={{ font: 'var(--t-caption)', color: 'var(--vg-accent)', marginTop: 24 }}>vianigram.me · faq · privacy</div>
          </div>
        )}
      </VG.Pivot>
    </div>
  );
}

function NotificationsSettingsScreen({ onBack }) {
  const [a, sa] = useStateP(true), [b, sb] = useStateP(true), [c, sc] = useStateP(false), [d, sd] = useStateP(true);
  const [preview, sp] = useStateP(true), [inApp, si] = useStateP(true);
  return (
    <div className="vg-phone" data-screen-label="23 notif settings">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="notifications" />
      <div style={{ flex: 1, overflowY: 'auto', paddingBottom: 12 }}>
        <div style={{ padding: '8px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>message notifications</div>
        <VG.SettingsRow label="private chats" right={<VG.Toggle value={a} onChange={sa} />} chevron={false} />
        <VG.SettingsRow label="groups" right={<VG.Toggle value={b} onChange={sb} />} chevron={false} />
        <VG.SettingsRow label="channels" right={<VG.Toggle value={c} onChange={sc} />} chevron={false} />
        <VG.SettingsRow label="calls" right={<VG.Toggle value={d} onChange={sd} />} chevron={false} />
        <div style={{ padding: '20px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>sound</div>
        <VG.SettingsRow label="message tone" value="signal" />
        <VG.SettingsRow label="vibration" value="default" />
        <div style={{ padding: '20px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>preview</div>
        <VG.SettingsRow label="message preview" sub="show text in toast" right={<VG.Toggle value={preview} onChange={sp} />} chevron={false} />
        <VG.SettingsRow label="in-app preview" right={<VG.Toggle value={inApp} onChange={si} />} chevron={false} />
      </div>
    </div>
  );
}

function PrivacySettingsScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="24 privacy settings">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="privacy" />
      <div style={{ flex: 1, overflowY: 'auto' }}>
        <div style={{ padding: '8px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>security</div>
        <VG.SettingsRow label="passcode lock" value="off" />
        <VG.SettingsRow label="two-step verification" value="on" />
        <VG.SettingsRow label="active sessions" sub="3 devices" />
        <VG.SettingsRow label="blocked users" value="2" />
        <div style={{ padding: '20px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>privacy</div>
        <VG.SettingsRow label="last seen" value="contacts" />
        <VG.SettingsRow label="phone number" value="contacts" />
        <VG.SettingsRow label="profile photo" value="everybody" />
        <VG.SettingsRow label="forwards" value="contacts" />
        <VG.SettingsRow label="who can call me" value="everybody" />
      </div>
    </div>
  );
}

function ActiveSessionsScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="25 active sessions">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="devices" />
      <div style={{ flex: 1, overflowY: 'auto', padding: '0 0 12px' }}>
        <div style={{ padding: '8px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>this device</div>
        <div style={{ padding: '12px 24px 16px', background: 'var(--vg-surface)', margin: '0 12px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 8 }}>
            <Glyph name="device" size={22} color="var(--vg-accent)" />
            <div>
              <div style={{ font: 'var(--t-row-title)' }}>windows phone</div>
              <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>vianigram 3.4.1 · windows 10 mobile</div>
            </div>
          </div>
          <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)' }}>tokyo, jp · 2402:6b00:… · active now</div>
        </div>
        <div style={{ padding: '20px 24px 4px', font: 'var(--t-caption)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>other sessions</div>
        {[
          { dev: 'macbook pro', app: 'vianigram desktop 3.4', loc: 'tokyo · 2 hours ago' },
          { dev: 'pixel 7', app: 'vianigram android 3.3', loc: 'kyoto · yesterday' },
        ].map((s, i) => (
          <Tilt key={i} className="vg-row" style={{ minHeight: 64, alignItems: 'flex-start', padding: '12px 24px' }}>
            <Glyph name="device" size={20} color="var(--vg-fg-2)" style={{ marginTop: 4 }} />
            <div className="vg-row-content">
              <div style={{ font: 'var(--t-row-title)' }}>{s.dev}</div>
              <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)' }}>{s.app}</div>
              <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)' }}>{s.loc}</div>
            </div>
            <div style={{ font: 'var(--t-meta)', color: 'var(--vg-danger)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>terminate</div>
          </Tilt>
        ))}
        <div style={{ padding: '24px', textAlign: 'center' }}>
          <div style={{ font: 'var(--t-row-title)', color: 'var(--vg-danger)' }}>terminate all other sessions</div>
        </div>
      </div>
    </div>
  );
}

window.SettingsScreens = {
  SelfProfileScreen, OtherProfileScreen, GroupInfoScreen,
  SettingsRootScreen, NotificationsSettingsScreen, PrivacySettingsScreen, ActiveSessionsScreen
};
