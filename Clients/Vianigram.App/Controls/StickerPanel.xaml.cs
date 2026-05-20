// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class StickerPanel : UserControl
    {
        public static readonly DependencyProperty PacksProperty =
            DependencyProperty.Register("Packs", typeof(IEnumerable), typeof(StickerPanel),
                new PropertyMetadata(null, OnPacksChanged));

        public static readonly DependencyProperty StickersProperty =
            DependencyProperty.Register("Stickers", typeof(IEnumerable), typeof(StickerPanel),
                new PropertyMetadata(null, OnStickersChanged));

        public IEnumerable Packs
        {
            get { return (IEnumerable)GetValue(PacksProperty); }
            set { SetValue(PacksProperty, value); }
        }

        public IEnumerable Stickers
        {
            get { return (IEnumerable)GetValue(StickersProperty); }
            set { SetValue(StickersProperty, value); }
        }

        public event EventHandler<object> OnStickerSelected;

        public StickerPanel()
        {
            this.InitializeComponent();
            ApplyPacks();
            ApplyStickers();
            StickerGrid.ItemClick += OnStickerClick;
        }

        private static void OnPacksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = d as StickerPanel;
            if (p != null) p.ApplyPacks();
        }

        private static void OnStickersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = d as StickerPanel;
            if (p != null) p.ApplyStickers();
        }

        private void ApplyPacks()
        {
            if (PackTabs != null) PackTabs.ItemsSource = Packs;
        }

        private void ApplyStickers()
        {
            if (StickerGrid == null || EmptyText == null) return;
            StickerGrid.ItemsSource = Stickers;
            bool empty = Stickers == null || !HasItems(Stickers);
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool HasItems(IEnumerable e)
        {
            if (e == null) return false;
            var en = e.GetEnumerator();
            try
            {
                return en.MoveNext();
            }
            finally
            {
                var disp = en as IDisposable;
                if (disp != null) disp.Dispose();
            }
        }

        private void OnStickerClick(object sender, ItemClickEventArgs e)
        {
            var handler = OnStickerSelected;
            if (handler != null) handler(this, e.ClickedItem);
        }
    }
}
