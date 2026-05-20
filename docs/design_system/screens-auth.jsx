// Vianigram — Auth flow screens (5)
const { useState: useStateA, useEffect: useEffectA } = React;

// Welcome / start
function WelcomeScreen({ onNext, onQR }) {
  // Floating bubbles + particles for the hero — gives the dead space life
  const bubbles = [
    { x: 28, y: 86, w: 180, t: 'hey from kyoto 🌸', out: false, delay: 0 },
    { x: 142, y: 138, w: 150, t: 'photo · 2.4 mb', out: true, delay: 0.4 },
    { x: 38, y: 188, w: 200, t: 'voice · 0:12 ▶', out: false, delay: 0.8 },
    { x: 168, y: 240, w: 130, t: 'on my way ✓✓', out: true, delay: 1.2 }
  ];
  return (
    <div className="vg-phone" data-screen-label="01 welcome" style={{ position: 'relative', overflow: 'hidden' }}>
      <VG.StatusBar />
      {/* Ambient diagonal accent wash */}
      <div style={{
        position: 'absolute', inset: 0, pointerEvents: 'none',
        background: 'radial-gradient(ellipse 380px 220px at 80% 18%, color-mix(in oklab, var(--vg-accent) 28%, transparent), transparent 70%)'
      }} />
      {/* Floating message bubble cluster */}
      <div style={{ position: 'absolute', top: 60, left: 0, right: 0, height: 300, pointerEvents: 'none' }}>
        {bubbles.map((b, i) => (
          <div key={i} style={{
            position: 'absolute', left: b.x, top: b.y, width: b.w,
            background: b.out ? 'var(--vg-accent)' : 'var(--vg-surface)',
            color: b.out ? '#fff' : 'var(--vg-fg)',
            padding: '6px 10px', font: '400 12px/16px var(--vg-font)',
            opacity: 0, animation: `vg-welcome-float 800ms ${b.delay}s cubic-bezier(.2,.7,.3,1) forwards`
          }}>{b.t}</div>
        ))}
        {/* Diamond glyph nucleus */}
        <div style={{
          position: 'absolute', top: 8, left: '50%', transform: 'translateX(-50%) rotate(45deg)',
          width: 56, height: 56, background: 'var(--vg-accent)',
          boxShadow: '0 0 0 8px color-mix(in oklab, var(--vg-accent) 25%, transparent), 0 0 0 18px color-mix(in oklab, var(--vg-accent) 12%, transparent)',
          animation: 'vg-welcome-pulse 2.4s ease-in-out infinite'
        }} />
      </div>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', padding: '60px 24px 32px', position: 'relative', zIndex: 1 }}>
        <div style={{ marginBottom: 28 }}>
          {/* Disclaimer pill — third-party telegram client */}
          <div style={{
            display: 'inline-flex', alignItems: 'center', gap: 6,
            padding: '4px 10px', background: 'var(--vg-surface)',
            font: '400 10px/14px var(--vg-font)', color: 'var(--vg-fg-2)',
            letterSpacing: '0.06em', textTransform: 'uppercase', marginBottom: 14
          }}>
            <span style={{ width: 6, height: 6, background: 'var(--vg-accent)', transform: 'rotate(45deg)' }} />
            third-party telegram client
          </div>
          {/* Wordmark */}
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 14 }}>
            <div style={{ font: '300 44px/1 var(--vg-font)', letterSpacing: '-0.02em' }}>vianigram</div>
            <div style={{ font: '300 13px/1 var(--vg-font)', color: 'var(--vg-fg-3)' }}>3.4</div>
          </div>
          <div style={{ font: '300 17px/22px var(--vg-font)', color: 'var(--vg-fg-2)', maxWidth: 300, textWrap: 'pretty' }}>
            an unofficial telegram client built native for windows phone — fast pivots, tilt, and the metro you remember.
          </div>
          <div style={{ display: 'flex', gap: 14, marginTop: 14, font: 'var(--t-meta)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
            <span>· mtproto 2.0</span><span>· e2ee secret chats</span><span>· open source</span>
          </div>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <button className="vg-btn vg-btn-primary vg-btn-block" onClick={onNext}>continue with telegram</button>
          <div style={{ display: 'flex', gap: 12 }}>
            <button className="vg-btn" style={{ flex: 1, borderColor: 'var(--vg-fg-3)', color: 'var(--vg-fg-2)' }} onClick={onQR}>scan qr</button>
            <button className="vg-btn" style={{ flex: 1, borderColor: 'var(--vg-fg-3)', color: 'var(--vg-fg-2)' }}>english ▾</button>
          </div>
          <div style={{ textAlign: 'center', font: '400 10px/14px var(--vg-font)', color: 'var(--vg-fg-3)', marginTop: 8, maxWidth: 260, alignSelf: 'center' }}>
            not affiliated with telegram fz-llc. uses the official telegram api under tos.
          </div>
        </div>
      </div>
    </div>
  );
}

// Phone entry
function PhoneScreen({ onNext, onBack }) {
  const [country, setCountry] = useStateA({ flag: 'JP', code: '+81', name: 'Japan' });
  const [phone, setPhone] = useStateA('90 1234 5678');
  return (
    <div className="vg-phone" data-screen-label="02 phone">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="your number" />
      <div style={{ padding: '20px 24px', flex: 1 }}>
        <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)', marginBottom: 24 }}>
          please confirm your country code and enter your phone number.
        </div>
        <Tilt className="vg-srow" style={{ padding: '14px 0', borderBottom: '1px solid var(--vg-divider)' }}>
          <div style={{ flex: 1 }}>
            <div style={{ font: 'var(--t-meta)', color: 'var(--vg-fg-3)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>country</div>
            <div style={{ font: 'var(--t-body)', marginTop: 2 }}>{country.name}</div>
          </div>
          <Glyph name="chevron" size={14} color="var(--vg-fg-3)" />
        </Tilt>
        <div style={{ display: 'flex', gap: 12, marginTop: 20, alignItems: 'baseline' }}>
          <div style={{ minWidth: 60, font: '300 22px/1 var(--vg-font)' }}>{country.code}</div>
          <input className="vg-field" style={{ flex: 1, background: 'transparent', borderTop: 0, borderLeft: 0, borderRight: 0, borderBottom: '2px solid var(--vg-fg-3)', padding: '8px 0', fontSize: 22, fontWeight: 300 }}
            value={phone} onChange={e => setPhone(e.target.value)} />
        </div>
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'forward', label: 'next', onClick: onNext }
      ]} onMore={() => {}} />
    </div>
  );
}

// SMS code
function SmsScreen({ onNext, onBack }) {
  const [digits, setDigits] = useStateA(['7', '3', '4', '', '']);
  const [seconds, setSeconds] = useStateA(48);
  useEffectA(() => {
    if (seconds <= 0) return;
    const t = setTimeout(() => setSeconds(s => s - 1), 1000);
    return () => clearTimeout(t);
  }, [seconds]);
  return (
    <div className="vg-phone" data-screen-label="03 sms">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="enter code" />
      <div style={{ padding: '20px 24px', flex: 1 }}>
        <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)', marginBottom: 28 }}>
          we sent a 5-digit code to<br/>
          <span style={{ color: 'var(--vg-fg)' }}>+81 90 1234 5678</span>{' '}
          <span style={{ color: 'var(--vg-accent)' }}>edit number</span>
        </div>
        <div style={{ display: 'flex', gap: 14, marginBottom: 32 }}>
          {digits.map((d, i) => (
            <div key={i} style={{
              flex: 1, height: 64, borderBottom: `2px solid ${d ? 'var(--vg-accent)' : 'var(--vg-fg-3)'}`,
              font: '300 36px/64px var(--vg-font)', textAlign: 'center'
            }}>{d}</div>
          ))}
        </div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)', marginBottom: 12 }}>
          {seconds > 0 ? `resend code in 0:${String(seconds).padStart(2, '0')}` : <span style={{ color: 'var(--vg-accent)' }}>resend code</span>}
        </div>
        <ProgressDots />
      </div>
      <VG.AppBar buttons={[{ glyph: 'back', label: 'back', onClick: onBack }]} onMore={() => {}} />
    </div>
  );
}

// 2FA password
function TwoFAScreen({ onNext, onBack }) {
  const [pw, setPw] = useStateA('');
  return (
    <div className="vg-phone" data-screen-label="04 2fa">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="password" />
      <div style={{ padding: '20px 24px', flex: 1 }}>
        <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)', marginBottom: 24 }}>
          this account is protected by two-step verification. enter your cloud password to continue.
        </div>
        <input className="vg-field" type="password" placeholder="password" value={pw}
          onChange={e => setPw(e.target.value)}
          style={{ background: 'transparent', borderTop: 0, borderLeft: 0, borderRight: 0, borderBottom: '2px solid var(--vg-fg-3)', padding: '12px 0', fontSize: 18 }} />
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-3)', marginTop: 10 }}>hint: my first cat</div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-accent)', marginTop: 28 }}>forgot password?</div>
      </div>
      <VG.AppBar buttons={[
        { glyph: 'back', label: 'back', onClick: onBack },
        { glyph: 'check', label: 'submit', onClick: onNext }
      ]} onMore={() => {}} />
    </div>
  );
}

// QR login
function QRScreen({ onBack, onPhone }) {
  return (
    <div className="vg-phone" data-screen-label="05 qr">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="scan qr" />
      <div style={{ padding: '20px 24px', flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)', textAlign: 'center', marginBottom: 24 }}>
          open vianigram on another device.<br/>
          go to settings → devices → link device.
        </div>
        {/* Stylized QR */}
        <div style={{ width: 220, height: 220, background: '#fff', padding: 12, marginBottom: 20 }}>
          <div style={{ width: '100%', height: '100%', position: 'relative', background: '#000' }}>
            {Array.from({ length: 14 }).map((_, r) =>
              Array.from({ length: 14 }).map((_, c) => {
                const corner = (r < 3 && c < 3) || (r < 3 && c > 10) || (r > 10 && c < 3);
                const fill = corner ? ((r === 0 || r === 2 || c === 0 || c === 2) || (r === 1 && c === 1)) : Math.random() > 0.5;
                return fill ? <div key={`${r}-${c}`} style={{
                  position: 'absolute', width: '7.14%', height: '7.14%',
                  left: `${c * 7.14}%`, top: `${r * 7.14}%`, background: '#fff'
                }} /> : null;
              })
            )}
            {/* Center diamond logo */}
            <div style={{ position: 'absolute', inset: '40% 40%', background: 'var(--vg-accent)', transform: 'rotate(45deg)' }} />
          </div>
        </div>
        <div style={{ font: 'var(--t-caption)', color: 'var(--vg-fg-2)', marginBottom: 4 }}>waiting for scan…</div>
        <ProgressDots />
        <div style={{ marginTop: 'auto', font: 'var(--t-body)', color: 'var(--vg-accent)' }} onClick={onPhone}>use phone number</div>
      </div>
      <VG.AppBar buttons={[{ glyph: 'back', label: 'back', onClick: onBack }]} onMore={() => {}} />
    </div>
  );
}

window.AuthScreens = { WelcomeScreen, PhoneScreen, SmsScreen, TwoFAScreen, QRScreen };
