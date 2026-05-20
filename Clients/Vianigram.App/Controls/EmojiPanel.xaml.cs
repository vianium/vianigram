// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class EmojiPanel : UserControl
    {
        private static readonly Dictionary<string, string[]> Catalog = BuildCatalog();

        public static readonly DependencyProperty SelectedCategoryProperty =
            DependencyProperty.Register("SelectedCategory", typeof(string), typeof(EmojiPanel),
                new PropertyMetadata("Smileys", OnSelectedCategoryChanged));

        public string SelectedCategory
        {
            get { return (string)GetValue(SelectedCategoryProperty); }
            set { SetValue(SelectedCategoryProperty, value); }
        }

        public event EventHandler<string> OnEmojiSelected;

        public EmojiPanel()
        {
            this.InitializeComponent();
            ApplyCategory();
            EmojiGrid.ItemClick += OnEmojiClick;
            HookTabs();
        }

        private static void OnSelectedCategoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = d as EmojiPanel;
            if (p != null) p.ApplyCategory();
        }

        private void ApplyCategory()
        {
            if (EmojiGrid == null) return;
            string key = SelectedCategory ?? "Smileys";
            string[] items;
            if (!Catalog.TryGetValue(key, out items)) items = Catalog["Smileys"];
            EmojiGrid.ItemsSource = items;
        }

        private void HookTabs()
        {
            TabSmileys.Click += TabClick;
            TabPeople.Click += TabClick;
            TabAnimals.Click += TabClick;
            TabFood.Click += TabClick;
            TabActivities.Click += TabClick;
            TabTravel.Click += TabClick;
            TabObjects.Click += TabClick;
            TabSymbols.Click += TabClick;
            TabFlags.Click += TabClick;
        }

        private void TabClick(object sender, RoutedEventArgs e)
        {
            var b = sender as Button;
            if (b == null) return;
            string tag = b.Tag as string;
            if (!string.IsNullOrEmpty(tag)) SelectedCategory = tag;
        }

        private void OnEmojiClick(object sender, ItemClickEventArgs e)
        {
            string s = e.ClickedItem as string;
            var handler = OnEmojiSelected;
            if (handler != null) handler(this, s);
        }

        private static Dictionary<string, string[]> BuildCatalog()
        {
            var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            d["Smileys"] = new[] { "😀", "😁", "😂", "🤣", "😃", "😄", "😅", "😆", "😉", "😊", "😋", "😎", "😍", "😘", "😗", "🙂", "🤗", "🤔", "😐", "😑", "😶", "🙄", "😏", "😣", "😥", "😮" };
            d["People"] = new[] { "👋", "🤚", "🖐", "✋", "🖖", "👌", "🤞", "✌", "🤟", "🤘", "👈", "👉", "👆", "👇", "☝", "👍", "👎", "✊", "👊", "🤛", "🤜", "👏", "🙌", "👐", "🤲", "🙏" };
            d["Animals"] = new[] { "🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐨", "🐯", "🦁", "🐮", "🐷", "🐸", "🐵", "🐔", "🐧", "🐦", "🦆", "🦅", "🦉", "🦇", "🐺", "🐗", "🐴", "🦄" };
            d["Food"] = new[] { "🍏", "🍎", "🍐", "🍊", "🍋", "🍌", "🍉", "🍇", "🍓", "🍈", "🍒", "🍑", "🍍", "🥥", "🥝", "🍅", "🍆", "🥑", "🥦", "🥒", "🌶", "🌽", "🥕", "🥔", "🍠", "🥐" };
            d["Activities"] = new[] { "⚽", "🏀", "🏈", "⚾", "🎾", "🏐", "🏉", "🎱", "🏓", "🏸", "🥅", "🏒", "🏑", "🏏", "⛳", "🏹", "🎣", "🥊", "🥋", "⛸", "🎿", "⛷", "🏂", "🏋", "🤸", "🤺" };
            d["Travel"] = new[] { "🚗", "🚕", "🚙", "🚌", "🚎", "🏎", "🚓", "🚑", "🚒", "🚐", "🚚", "🚛", "🚜", "🛴", "🚲", "🛵", "🏍", "🚨", "🚔", "🚍", "🚘", "🚖", "🚡", "🚠", "🚟", "🚃" };
            d["Objects"] = new[] { "💡", "🔦", "🕯", "📞", "📱", "💻", "🖥", "🖨", "⌨", "🖱", "🖲", "💾", "💿", "📀", "🎥", "📷", "📹", "📺", "📻", "🎙", "🎚", "🎛", "⏰", "⏲", "⌛", "⏳" };
            d["Symbols"] = new[] { "❤", "🧡", "💛", "💚", "💙", "💜", "🖤", "💔", "❣", "💕", "💞", "💓", "💗", "💖", "💘", "💝", "💟", "☮", "✝", "☪", "🕉", "☸", "✡", "🔯", "🕎", "☯" };
            d["Flags"] = new[] { "🏁", "🚩", "🎌", "🏴", "🏳", "🏳️‍🌈", "🇺🇸", "🇲🇽", "🇨🇦", "🇧🇷", "🇦🇷", "🇨🇱", "🇨🇴", "🇪🇸", "🇫🇷", "🇩🇪", "🇮🇹", "🇬🇧", "🇯🇵", "🇨🇳", "🇰🇷", "🇮🇳", "🇷🇺", "🇿🇦", "🇦🇺", "🇳🇿" };
            return d;
        }
    }
}
