// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Vianigram.App.Controls
{
    public sealed partial class ChatListItem : UserControl
    {
        public static readonly DependencyProperty AvatarSourceProperty =
            DependencyProperty.Register("AvatarSource", typeof(string), typeof(ChatListItem),
                new PropertyMetadata(null, OnAvatarChanged));

        // Direct BitmapImage binding — takes precedence over the URL-based
        // AvatarSource. Bound from DialogRow.AvatarBitmap.
        public static readonly DependencyProperty AvatarBitmapProperty =
            DependencyProperty.Register("AvatarBitmap", typeof(ImageSource), typeof(ChatListItem),
                new PropertyMetadata(null, OnAvatarChanged));

        public static readonly DependencyProperty InitialsProperty =
            DependencyProperty.Register("Initials", typeof(string), typeof(ChatListItem),
                new PropertyMetadata("", OnAvatarChanged));

        public static readonly DependencyProperty AvatarColorSeedProperty =
            DependencyProperty.Register("AvatarColorSeed", typeof(long), typeof(ChatListItem),
                new PropertyMetadata(0L, OnAvatarChanged));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(ChatListItem),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty LastMessagePreviewProperty =
            DependencyProperty.Register("LastMessagePreview", typeof(string), typeof(ChatListItem),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty TimestampProperty =
            DependencyProperty.Register("Timestamp", typeof(string), typeof(ChatListItem),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty UnreadCountProperty =
            DependencyProperty.Register("UnreadCount", typeof(int), typeof(ChatListItem),
                new PropertyMetadata(0, OnUnreadChanged));

        public static readonly DependencyProperty IsPinnedProperty =
            DependencyProperty.Register("IsPinned", typeof(bool), typeof(ChatListItem),
                new PropertyMetadata(false, OnFlagsChanged));

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register("IsMuted", typeof(bool), typeof(ChatListItem),
                new PropertyMetadata(false, OnFlagsChanged));

        public static readonly DependencyProperty OnlineStatusProperty =
            DependencyProperty.Register("OnlineStatus", typeof(string), typeof(ChatListItem),
                new PropertyMetadata("", OnFlagsChanged));

        public string AvatarSource
        {
            get { return (string)GetValue(AvatarSourceProperty); }
            set { SetValue(AvatarSourceProperty, value); }
        }

        public ImageSource AvatarBitmap
        {
            get { return (ImageSource)GetValue(AvatarBitmapProperty); }
            set { SetValue(AvatarBitmapProperty, value); }
        }

        public string Initials
        {
            get { return (string)GetValue(InitialsProperty); }
            set { SetValue(InitialsProperty, value); }
        }

        public long AvatarColorSeed
        {
            get { return (long)GetValue(AvatarColorSeedProperty); }
            set { SetValue(AvatarColorSeedProperty, value); }
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string LastMessagePreview
        {
            get { return (string)GetValue(LastMessagePreviewProperty); }
            set { SetValue(LastMessagePreviewProperty, value); }
        }

        public string Timestamp
        {
            get { return (string)GetValue(TimestampProperty); }
            set { SetValue(TimestampProperty, value); }
        }

        public int UnreadCount
        {
            get { return (int)GetValue(UnreadCountProperty); }
            set { SetValue(UnreadCountProperty, value); }
        }

        public bool IsPinned
        {
            get { return (bool)GetValue(IsPinnedProperty); }
            set { SetValue(IsPinnedProperty, value); }
        }

        public bool IsMuted
        {
            get { return (bool)GetValue(IsMutedProperty); }
            set { SetValue(IsMutedProperty, value); }
        }

        public string OnlineStatus
        {
            get { return (string)GetValue(OnlineStatusProperty); }
            set { SetValue(OnlineStatusProperty, value); }
        }

        public ChatListItem()
        {
            this.InitializeComponent();
            ApplyAvatar();
            ApplyText();
            ApplyUnread();
            ApplyFlags();
        }

        private static void OnAvatarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = d as ChatListItem;
            if (c != null) c.ApplyAvatar();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = d as ChatListItem;
            if (c != null) c.ApplyText();
        }

        private static void OnUnreadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = d as ChatListItem;
            if (c != null) c.ApplyUnread();
        }

        private static void OnFlagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = d as ChatListItem;
            if (c != null) c.ApplyFlags();
        }

        private void ApplyAvatar()
        {
            if (Avatar == null) return;
            Avatar.Image = AvatarBitmap; // wave F path (preferred)
            Avatar.ImageSource = AvatarSource; // legacy URL path
            Avatar.Initials = Initials ?? string.Empty;
            Avatar.ColorSeed = AvatarColorSeed;
        }

        private void ApplyText()
        {
            if (TitleText != null) TitleText.Text = Title ?? string.Empty;
            if (PreviewText != null) PreviewText.Text = LastMessagePreview ?? string.Empty;
            if (TimeText != null) TimeText.Text = Timestamp ?? string.Empty;
        }

        private void ApplyUnread()
        {
            if (UnreadBadgeControl != null) UnreadBadgeControl.Count = UnreadCount;
        }

        private void ApplyFlags()
        {
            if (PinnedGlyph != null) PinnedGlyph.Visibility = IsPinned ? Visibility.Visible : Visibility.Collapsed;
            if (MutedGlyph != null) MutedGlyph.Visibility = IsMuted ? Visibility.Visible : Visibility.Collapsed;
            if (OnlineDot != null)
            {
                bool online = string.Equals(OnlineStatus ?? string.Empty, "Online", System.StringComparison.OrdinalIgnoreCase);
                OnlineDot.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
