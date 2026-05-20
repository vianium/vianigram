// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed class MediaPickerSelection : EventArgs
    {
        public string Kind { get; private set; }
        public object Item { get; private set; }
        public MediaPickerSelection(string kind, object item) { Kind = kind; Item = item; }
    }

    public sealed partial class MediaPicker : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(MediaPicker),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public event EventHandler<MediaPickerSelection> SelectionMade;

        public MediaPicker()
        {
            this.InitializeComponent();
            ApplyItems();
            CameraButton.Click += OnSourceButtonClick;
            FileButton.Click += OnSourceButtonClick;
            RecentItemsGrid.ItemClick += OnItemClick;
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = d as MediaPicker;
            if (p != null) p.ApplyItems();
        }

        private void ApplyItems()
        {
            if (RecentItemsGrid != null) RecentItemsGrid.ItemsSource = ItemsSource;
        }

        private void OnSourceButtonClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var handler = SelectionMade;
            if (handler != null) handler(this, new MediaPickerSelection(btn.Tag as string, null));
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            var handler = SelectionMade;
            if (handler != null) handler(this, new MediaPickerSelection("Gallery", e.ClickedItem));
        }
    }
}
