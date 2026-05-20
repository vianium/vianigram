// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed partial class StickerBubble : UserControl
    {
        public static readonly DependencyProperty StickerImageSourceProperty =
            DependencyProperty.Register("StickerImageSource", typeof(string), typeof(StickerBubble),
                new PropertyMetadata("", OnSourceChanged));

        public static readonly DependencyProperty StickerWidthProperty =
            DependencyProperty.Register("StickerWidth", typeof(double), typeof(StickerBubble),
                new PropertyMetadata(192.0, OnSizeChanged));

        public static readonly DependencyProperty StickerHeightProperty =
            DependencyProperty.Register("StickerHeight", typeof(double), typeof(StickerBubble),
                new PropertyMetadata(192.0, OnSizeChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(StickerBubble),
                new PropertyMetadata(false, OnIsOutgoingChanged));

        public string StickerImageSource
        {
            get { return (string)GetValue(StickerImageSourceProperty); }
            set { SetValue(StickerImageSourceProperty, value); }
        }

        public double StickerWidth
        {
            get { return (double)GetValue(StickerWidthProperty); }
            set { SetValue(StickerWidthProperty, value); }
        }

        public double StickerHeight
        {
            get { return (double)GetValue(StickerHeightProperty); }
            set { SetValue(StickerHeightProperty, value); }
        }

        public bool IsOutgoing
        {
            get { return (bool)GetValue(IsOutgoingProperty); }
            set { SetValue(IsOutgoingProperty, value); }
        }

        public StickerBubble()
        {
            this.InitializeComponent();
            ApplySource();
            ApplySize();
            ApplyAlignment();
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as StickerBubble;
            if (b != null) b.ApplySource();
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as StickerBubble;
            if (b != null) b.ApplySize();
        }

        private static void OnIsOutgoingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as StickerBubble;
            if (b != null) b.ApplyAlignment();
        }

        private void ApplySource()
        {
            if (StickerImage == null || PlaceholderRect == null) return;
            string src = StickerImageSource;
            if (string.IsNullOrEmpty(src))
            {
                StickerImage.Source = null;
                StickerImage.Visibility = Visibility.Collapsed;
                PlaceholderRect.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                Uri uri;
                if (Uri.TryCreate(src, UriKind.Absolute, out uri) ||
                    Uri.TryCreate(src, UriKind.RelativeOrAbsolute, out uri))
                {
                    StickerImage.Source = new BitmapImage(uri);
                    StickerImage.Visibility = Visibility.Visible;
                    PlaceholderRect.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch
            {
            }

            StickerImage.Source = null;
            StickerImage.Visibility = Visibility.Collapsed;
            PlaceholderRect.Visibility = Visibility.Visible;
        }

        private void ApplySize()
        {
            if (StickerHost == null) return;
            StickerHost.Width = StickerWidth;
            StickerHost.Height = StickerHeight;
        }

        private void ApplyAlignment()
        {
            if (RootGrid != null)
            {
                RootGrid.HorizontalAlignment = IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
        }
    }
}
