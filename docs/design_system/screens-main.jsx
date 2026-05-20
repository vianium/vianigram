// Vianigram — Main shell (chat list) + Conversation screens

const { useState: useStateS, useEffect: useEffectS } = React;

// Chat list (Pivot root)
function ChatListScreen({ onOpenChat, state = 'normal' }) {
  const [active, setActive] = useStateS('all');
  const sections = [
    { id: 'all', label: 'all' },
    { id: 'unread', label: 'unread' },
    { id: 'personal', label: 'personal' },
    { id: 'groups', label: 'groups' }
  ];
  const all = VG.SAMPLE_CHATS;
  const filtered = {
    all, unread: all.filter(c => c.unread > 0),
    personal: all.filter(c => !c.group && !c.channel),
    groups: all.filter(c => c.group || c.channel)
  }[active];

  return (
    <div className="vg-phone" data-screen-label="06 chat list">
      <VG.StatusBar />
      <VG.TitleBar pageTitle="chats" />
      <VG.Pivot sections={sections} active={active} onChange={setActive}>
        {state === 'empty' && active === 'all' ? (
          <div style={{ padding: '60px 24px', textAlign: 'center' }}>
            <div style={{ width: 56, height: 56, background: 'var(--vg-accent)', transform: 'rotate(45deg)', margin: '0 auto 24px', opacity: 0.2 }} />
            <div style={{ font: '300 22px/26px var(--vg-font)', marginBottom: 8 }}>no chats yet</div>
            <div style={{ font: 'var(--t-body)', color: 'var(--vg-fg-2)' }}>start a conversation from the new-chat button below.</div>
          </div>
        ) : state === 'loading' ? (
          <div style={{ padding: '40px 0' }}><ProgressDots /></div>
        ) : (
          filtered.map(c => <VG.ChatRow key={c.id} chat={c} onClick={() => onOpenChat?.(c.id)} />)
        )}
      </VG.Pivot>
      <VG.AppBar buttons={[
        { glyph: 'search', label: 'search' },
        { glyph: 'edit', label: 'new chat' }
      ]} onMore={() => {}} />
    </div>
  );
}

// Conversation 1:1
function ConversationScreen({ onBack, peer = { name: 'Mira Sato', sub: 'last seen 2 hours ago' }, state = 'normal' }) {
  return (
    <div className="vg-phone" data-screen-label="07 chat">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content">
          <VG.Avatar name={peer.name} size={40} />
          <div style={{ minWidth: 0 }}>
            <div className="vg-topbar-title">{peer.name}</div>
            <div className="vg-topbar-sub">{peer.sub}</div>
          </div>
        </div>
        <button className="vg-compose-btn"><Glyph name="phone" size={18} /></button>
        <button className="vg-compose-btn"><Glyph name="video" size={18} /></button>
      </div>
      <div style={{ flex: 1, overflowY: 'auto', padding: '6px 0' }}>
        {state === 'loading' && <div style={{ padding: '20px 0' }}><ProgressDots /></div>}
        {state === 'error' && (
          <div style={{ padding: '40px 24px', textAlign: 'center' }}>
            <div style={{ color: 'var(--vg-danger)', font: 'var(--t-body)', marginBottom: 8 }}>couldn't load messages</div>
            <div style={{ color: 'var(--vg-accent)', font: 'var(--t-body)' }}>retry</div>
          </div>
        )}
        {state === 'empty' && (
          <div style={{ padding: '80px 24px', textAlign: 'center', color: 'var(--vg-fg-2)' }}>
            <div style={{ font: 'var(--t-body)' }}>no messages yet.<br/>say hi.</div>
          </div>
        )}
        {state === 'normal' && (
          <>
            <div className="vg-date-sep">today</div>
            {VG.SAMPLE_CONVO.map(m => (
              <div key={m.id} className={`vg-msg-row ${m.from === 'me' ? 'out' : 'in'}`}>
                <div className={`vg-bubble ${m.from === 'me' ? 'out' : 'in'}`}>
                  {m.reply && (
                    <div className="vg-reply-quote">
                      <div style={{ fontWeight: 600, fontSize: 12 }}>{m.reply.name}</div>
                      <div>{m.reply.text}</div>
                    </div>
                  )}
                  <span>{m.text}</span>
                  <span className="vg-bubble-time">
                    {m.time}{m.from === 'me' && (m.read ? ' ✓✓' : ' ✓')}
                  </span>
                </div>
              </div>
            ))}
          </>
        )}
      </div>
      <div className="vg-compose">
        <button className="vg-compose-btn"><Glyph name="emoji" size={18} /></button>
        <input className="vg-compose-field" placeholder="message" />
        <button className="vg-compose-btn"><Glyph name="attach" size={18} /></button>
        <button className="vg-compose-btn"><Glyph name="mic" size={18} /></button>
        <button className="vg-compose-btn send"><Glyph name="send" size={18} fill="currentColor" /></button>
      </div>
    </div>
  );
}

// Group chat
function GroupChatScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="08 group chat">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content">
          <VG.Avatar name="design crit" size={40} />
          <div style={{ minWidth: 0 }}>
            <div className="vg-topbar-title">design crit</div>
            <div className="vg-topbar-sub">8 members, 3 online</div>
          </div>
        </div>
        <button className="vg-compose-btn"><Glyph name="phone" size={18} /></button>
      </div>
      <div style={{ flex: 1, overflowY: 'auto', padding: '6px 0' }}>
        <div className="vg-date-sep">today</div>
        {VG.GROUP_CONVO.map(m => (
          <div key={m.id} className={`vg-msg-row ${m.from === 'me' ? 'out' : 'in'}`}>
            <div className={`vg-bubble ${m.from === 'me' ? 'out' : 'in'}`}>
              {m.sender && <div className="vg-msg-sender" style={{ color: VG.colorForName(m.sender) }}>{m.sender}</div>}
              <span>{m.text.split('@you').map((s, i, a) => (
                <React.Fragment key={i}>{s}{i < a.length - 1 && <span style={{ color: 'var(--vg-accent)' }}>@you</span>}</React.Fragment>
              ))}</span>
              <span className="vg-bubble-time">{m.time}{m.from === 'me' && ' ✓✓'}</span>
            </div>
          </div>
        ))}
      </div>
      <div className="vg-compose">
        <button className="vg-compose-btn"><Glyph name="emoji" size={18} /></button>
        <input className="vg-compose-field" placeholder="message" />
        <button className="vg-compose-btn"><Glyph name="attach" size={18} /></button>
        <button className="vg-compose-btn send"><Glyph name="send" size={18} fill="currentColor" /></button>
      </div>
    </div>
  );
}

// Channel
function ChannelScreen({ onBack }) {
  return (
    <div className="vg-phone" data-screen-label="09 channel">
      <VG.StatusBar />
      <div className="vg-topbar">
        <button className="vg-topbar-back" onClick={onBack}>‹</button>
        <div className="vg-topbar-content">
          <VG.Avatar name="Vianet" size={40} />
          <div>
            <div className="vg-topbar-title">vianet announcements</div>
            <div className="vg-topbar-sub">4,218 subscribers</div>
          </div>
        </div>
      </div>
      <div style={{ flex: 1, overflowY: 'auto', padding: '6px 0' }}>
        <div className="vg-date-sep">today</div>
        {VG.CHANNEL_POSTS.map(p => (
          <div key={p.id} className="vg-msg-row in">
            <div className="vg-bubble in" style={{ maxWidth: '92%' }}>
              <span style={{ whiteSpace: 'pre-wrap' }}>{p.text}</span>
              <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
                {p.reactions.map((r, i) => (
                  <div key={i} style={{ background: 'rgba(0,171,169,0.15)', padding: '2px 8px', font: 'var(--t-caption)', color: 'var(--vg-accent)' }}>
                    {r.e} {r.n}
                  </div>
                ))}
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 6 }}>
                <span className="vg-bubble-time" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                  <Glyph name="eye" size={11} /> {p.views}
                </span>
                <span className="vg-bubble-time">{p.time}</span>
              </div>
            </div>
          </div>
        ))}
      </div>
      <div style={{ background: 'var(--vg-surface)', padding: '14px 24px', textAlign: 'center', font: 'var(--t-body)', color: 'var(--vg-fg-2)', borderTop: '1px solid var(--vg-divider)' }}>
        <Glyph name="bell" size={14} /> mute
      </div>
    </div>
  );
}

window.MainScreens = { ChatListScreen, ConversationScreen, GroupChatScreen, ChannelScreen };
