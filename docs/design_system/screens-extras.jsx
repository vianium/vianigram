// Vianigram — Hero flow (interactive prototype) + Tokens / Components reference pages

const { useState: useStateH } = React;

// Hero flow — clickable: welcome → phone → sms → chat list → conversation
function HeroFlow() {
  const [stack, setStack] = useStateH(['welcome']);
  const top = stack[stack.length - 1];
  const push = (id) => setStack(s => [...s, id]);
  const pop = () => setStack(s => s.length > 1 ? s.slice(0, -1) : s);

  let screen;
  if (top === 'welcome') screen = <AuthScreens.WelcomeScreen onNext={() => push('phone')} onQR={() => push('qr')} />;
  else if (top === 'phone') screen = <AuthScreens.PhoneScreen onNext={() => push('sms')} onBack={pop} />;
  else if (top === 'sms') screen = <AuthScreens.SmsScreen onNext={() => push('list')} onBack={pop} />;
  else if (top === 'qr') screen = <AuthScreens.QRScreen onBack={pop} onPhone={() => { setStack(['welcome', 'phone']); }} />;
  else if (top === 'list') screen = <MainScreens.ChatListScreen onOpenChat={(id) => push('chat:' + id)} />;
  else if (top.startsWith('chat:')) {
    const id = top.slice(5);
    const chat = VG.SAMPLE_CHATS.find(c => c.id === id);
    if (chat?.channel) screen = <MainScreens.ChannelScreen onBack={pop} />;
    else if (chat?.group) screen = <MainScreens.GroupChatScreen onBack={pop} />;
    else screen = <MainScreens.ConversationScreen onBack={pop} peer={{ name: chat?.name || 'Mira Sato', sub: 'last seen 2 hours ago' }} />;
  }

  return (
    <div style={{ width: 360, height: 640, position: 'relative', overflow: 'hidden' }}>
      <div key={top} style={{
        position: 'absolute', inset: 0,
        animation: 'vg-page-in 320ms cubic-bezier(.2,.7,.3,1)'
      }}>
        {screen}
      </div>
    </div>
  );
}

// Tokens reference page
function TokensPage() {
  const accents = [
    { n: 'cyan', v: '#00ABA9' }, { n: 'mauve', v: '#7E3878' },
    { n: 'magenta', v: '#E51400' }, { n: 'lime', v: '#A4C400' },
    { n: 'orange', v: '#F09609' }, { n: 'blue', v: '#1BA1E2' },
    { n: 'green', v: '#60A917' }, { n: 'violet', v: '#AA00FF' }
  ];
  return (
    <div style={{ width: 1080, padding: 48, background: '#000', color: '#fff', font: 'var(--vg-font, sans-serif)', minHeight: 720 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 8 }}>
        <div style={{ width: 24, height: 24, background: '#00ABA9', transform: 'rotate(45deg)' }} />
        <div style={{ font: '300 14px/1 var(--vg-font)', letterSpacing: '0.18em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.6)' }}>VIANIGRAM</div>
      </div>
      <h1 style={{ font: '300 64px/1 var(--vg-font)', margin: '0 0 48px', letterSpacing: '-0.02em' }}>design tokens</h1>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 48 }}>
        <div>
          <div style={{ font: '300 28px/1 var(--vg-font)', marginBottom: 20 }}>typography</div>
          <div style={{ borderTop: '1px solid rgba(255,255,255,0.15)' }}>
            {[
              { l: 'page title', s: '300 40px', d: 'segoe ui light · 40 / 44 · sentence case' },
              { l: 'pivot header', s: '300 26px', d: 'semilight · active 100% / inactive 40%' },
              { l: 'app name', s: '300 14px upper', d: 'tracked uppercase · 14 / 18' },
              { l: 'row title', s: '400 17px', d: 'regular · 17 / 20' },
              { l: 'body', s: '400 15px', d: 'regular · 15 / 20' },
              { l: 'caption', s: '400 12px', d: 'regular · 12 / 16 · 60% opacity' }
            ].map(t => (
              <div key={t.l} style={{ display: 'flex', borderBottom: '1px solid rgba(255,255,255,0.1)', padding: '20px 0', gap: 24, alignItems: 'baseline' }}>
                <div style={{ width: 360 }}>
                  <div style={{ font: t.s.includes('upper') ? '300 14px/1 var(--vg-font)' : `${t.s.replace('upper','')}/1.1 var(--vg-font)`, textTransform: t.s.includes('upper') ? 'uppercase' : 'none', letterSpacing: t.s.includes('upper') ? '0.18em' : '-0.01em' }}>
                    {t.l}
                  </div>
                </div>
                <div style={{ font: '400 13px/18px var(--vg-font)', color: 'rgba(255,255,255,0.55)' }}>{t.d}</div>
              </div>
            ))}
          </div>
        </div>

        <div>
          <div style={{ font: '300 28px/1 var(--vg-font)', marginBottom: 20 }}>color</div>

          <div style={{ font: '300 14px/1 var(--vg-font)', letterSpacing: '0.18em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', margin: '0 0 12px' }}>dark theme</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8, marginBottom: 28 }}>
            {[['bg','#000'],['surface','#1F1F1F'],['fg','#FFF'],['fg 60%','rgba(255,255,255,0.6)']].map(([n,v]) => (
              <div key={n}><div style={{ height: 80, background: v, border: '1px solid rgba(255,255,255,0.1)' }} /><div style={{ font: 'var(--t-caption)', marginTop: 6, color: 'rgba(255,255,255,0.7)' }}>{n}</div></div>
            ))}
          </div>

          <div style={{ font: '300 14px/1 var(--vg-font)', letterSpacing: '0.18em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', margin: '0 0 12px' }}>light theme</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8, marginBottom: 28 }}>
            {[['bg','#FFF'],['surface','#F2F2F2'],['fg','#000'],['fg 60%','rgba(0,0,0,0.6)']].map(([n,v]) => (
              <div key={n}><div style={{ height: 80, background: v, border: '1px solid rgba(255,255,255,0.1)' }} /><div style={{ font: 'var(--t-caption)', marginTop: 6, color: 'rgba(255,255,255,0.7)' }}>{n}</div></div>
            ))}
          </div>

          <div style={{ font: '300 14px/1 var(--vg-font)', letterSpacing: '0.18em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', margin: '0 0 12px' }}>accents</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8 }}>
            {accents.map(a => (
              <div key={a.n}><div style={{ height: 56, background: a.v }} /><div style={{ font: 'var(--t-caption)', marginTop: 6, color: 'rgba(255,255,255,0.7)' }}>{a.n} <span style={{ color: 'rgba(255,255,255,0.4)' }}>{a.v}</span></div></div>
            ))}
          </div>
        </div>
      </div>

      <div style={{ marginTop: 48, font: '300 28px/1 var(--vg-font)', marginBottom: 20 }}>spacing & motion</div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 24, color: 'rgba(255,255,255,0.7)' }}>
        <div><div style={{ font: '300 32px/1 var(--vg-font)', color: '#fff' }}>24<span style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.5)' }}>px</span></div><div>page-title hang</div></div>
        <div><div style={{ font: '300 32px/1 var(--vg-font)', color: '#fff' }}>12<span style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.5)' }}>px</span></div><div>list-row inset</div></div>
        <div><div style={{ font: '300 32px/1 var(--vg-font)', color: '#fff' }}>80<span style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.5)' }}>px</span></div><div>list row min height</div></div>
        <div><div style={{ font: '300 32px/1 var(--vg-font)', color: '#fff' }}>±3°</div><div>tilt-press rotation</div></div>
      </div>
    </div>
  );
}

// Components reference page — shows each primitive at small scale
function ComponentsPage() {
  return (
    <div style={{ width: 1080, padding: 48, background: '#000', color: '#fff', minHeight: 600 }}>
      <div style={{ font: '300 14px/1 var(--vg-font)', letterSpacing: '0.18em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.6)', marginBottom: 8 }}>VIANIGRAM</div>
      <h1 style={{ font: '300 64px/1 var(--vg-font)', margin: '0 0 36px', letterSpacing: '-0.02em' }}>component library</h1>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 36 }}>

        <div>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>avatar</div>
          <div style={{ display: 'flex', gap: 12, alignItems: 'center', background: '#0a0a0a', padding: 20 }}>
            <VG.Avatar name="Mira Sato" size={56} />
            <VG.Avatar name="Holt Mendez" size={44} />
            <VG.Avatar name="Theo Park" size={36} />
            <VG.Avatar name="Anya Volkov" size={28} />
          </div>
        </div>

        <div>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>buttons</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12, background: '#0a0a0a', padding: 20 }}>
            <button className="vg-btn vg-btn-primary">primary action</button>
            <button className="vg-btn">secondary</button>
          </div>
        </div>

        <div>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>toggle</div>
          <div style={{ display: 'flex', gap: 24, background: '#0a0a0a', padding: 20, alignItems: 'center' }}>
            <VG.Toggle value={true} onChange={() => {}} />
            <VG.Toggle value={false} onChange={() => {}} />
            <span style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.5)' }}>WP-style toggleswitch</span>
          </div>
        </div>

        <div style={{ gridColumn: 'span 2' }}>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>list-item</div>
          <div style={{ background: '#000' }}>
            <VG.ChatRow chat={VG.SAMPLE_CHATS[0]} />
            <VG.ChatRow chat={VG.SAMPLE_CHATS[1]} />
            <VG.ChatRow chat={VG.SAMPLE_CHATS[3]} />
          </div>
        </div>

        <div>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>message bubble</div>
          <div style={{ background: '#000', padding: '12px 0' }}>
            <div className="vg-msg-row in"><div className="vg-bubble in">hey — still on for tonight?<span className="vg-bubble-time">9:14</span></div></div>
            <div className="vg-msg-row out"><div className="vg-bubble out">yes! 7pm at the rooftop<span className="vg-bubble-time">9:18 ✓✓</span></div></div>
          </div>
        </div>

        <div style={{ gridColumn: 'span 2' }}>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>app bar</div>
          <div style={{ background: '#000' }}>
            <VG.AppBar buttons={[
              { glyph: 'search', label: 'search' }, { glyph: 'edit', label: 'new chat' },
              { glyph: 'phone', label: 'call' }, { glyph: 'plus', label: 'invite' }
            ]} onMore={() => {}} />
          </div>
        </div>

        <div>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>progress</div>
          <div style={{ background: '#0a0a0a', padding: 24 }}>
            <ProgressDots />
            <div style={{ font: 'var(--t-caption)', color: 'rgba(255,255,255,0.5)', marginTop: 16 }}>5 dots, 2.4s loop, accent color</div>
          </div>
        </div>

        <div style={{ gridColumn: 'span 2' }}>
          <div style={{ font: 'var(--t-caption)', letterSpacing: '0.1em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.5)', marginBottom: 14 }}>glyphs (segoe mdl2-style)</div>
          <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', background: '#0a0a0a', padding: 20 }}>
            {['back','forward','search','edit','attach','mic','send','phone','video','bell','lock','users','channel','sticker','globe','chat','image','play','pause','check','doublecheck','pin','mute2','more','close','plus','camera','qr'].map(g => (
              <div key={g} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
                <Glyph name={g} size={20} />
                <span style={{ font: '400 9px/1 var(--vg-font)', color: 'rgba(255,255,255,0.4)' }}>{g}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

window.ExtraPages = { HeroFlow, TokensPage, ComponentsPage };
