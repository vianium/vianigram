// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.Controls
{
    public sealed partial class AvatarCircle : UserControl
    {
        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(255, 0, 171, 169),
            Color.FromArgb(255, 126, 56, 120),
            Color.FromArgb(255, 229, 20, 0),
            Color.FromArgb(255, 164, 196, 0),
            Color.FromArgb(255, 240, 150, 9),
            Color.FromArgb(255, 27, 161, 226),
            Color.FromArgb(255, 96, 169, 23),
            Color.FromArgb(255, 170, 0, 255)
        };

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(string), typeof(AvatarCircle),
                new PropertyMetadata(null, OnVisualChanged));

        // Direct BitmapImage binding for the inline-thumbnail path
        // (stripped JPEG → BitmapImage).
        // Takes precedence over ImageSource (URL) — when present, the
        // initials and color background are hidden. Use a generic
        // ImageSource type so callers can also bind a future
        // WriteableBitmap or RTM SoftwareBitmapSource.
        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register("Image", typeof(ImageSource), typeof(AvatarCircle),
                new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty InitialsProperty =
            DependencyProperty.Register("Initials", typeof(string), typeof(AvatarCircle),
                new PropertyMetadata("", OnVisualChanged));

        public static readonly DependencyProperty AvatarSizeProperty =
            DependencyProperty.Register("AvatarSize", typeof(double), typeof(AvatarCircle),
                new PropertyMetadata(48.0, OnSizeChanged));

        public static readonly DependencyProperty ColorSeedProperty =
            DependencyProperty.Register("ColorSeed", typeof(long), typeof(AvatarCircle),
                new PropertyMetadata(0L, OnVisualChanged));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Brush), typeof(AvatarCircle),
                new PropertyMetadata(null, OnVisualChanged));

        public string ImageSource
        {
            get { return (string)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        public ImageSource Image
        {
            get { return (ImageSource)GetValue(ImageProperty); }
            set { SetValue(ImageProperty, value); }
        }

        public string Initials
        {
            get { return (string)GetValue(InitialsProperty); }
            set { SetValue(InitialsProperty, value); }
        }

        public double AvatarSize
        {
            get { return (double)GetValue(AvatarSizeProperty); }
            set { SetValue(AvatarSizeProperty, value); }
        }

        public long ColorSeed
        {
            get { return (long)GetValue(ColorSeedProperty); }
            set { SetValue(ColorSeedProperty, value); }
        }

        public Brush BackgroundColor
        {
            get { return (Brush)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }

        public AvatarCircle()
        {
            this.InitializeComponent();
            ApplySize();
            ApplyVisual();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var a = d as AvatarCircle;
            if (a != null) a.ApplyVisual();
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var a = d as AvatarCircle;
            if (a != null) a.ApplySize();
        }

        private void ApplySize()
        {
            if (Host == null || BackgroundEllipse == null) return;
            double s = AvatarSize;
            Host.Width = s;
            Host.Height = s;
            BackgroundEllipse.Width = s;
            BackgroundEllipse.Height = s;
            if (InitialsText != null)
            {
                InitialsText.FontSize = s * 0.38;
            }
        }

        private void ApplyVisual()
        {
            if (BackgroundEllipse == null || InitialsText == null) return;

            if (TryApplyImage())
            {
                InitialsText.Visibility = Visibility.Collapsed;
                return;
            }

            BackgroundEllipse.Fill = BackgroundColor != null ? BackgroundColor : DeriveBackground();
            InitialsText.Text = Initials ?? string.Empty;
            InitialsText.Visibility = Visibility.Visible;
        }

        private bool TryApplyImage()
        {
            // Prefer the directly-supplied BitmapImage (e.g. an expanded
            // stripped thumb) over the URL-style ImageSource so we can
            // fill the avatar without an http fetch.
            ImageSource directImg = Image;
            if (directImg != null)
            {
                try
                {
                    var brush = new ImageBrush
                    {
                        ImageSource = directImg,
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                    BackgroundEllipse.Fill = brush;
                    return true;
                }
                catch
                {
                    // Fall through to URL / initials fallback.
                }
            }

            string src = ImageSource;
            if (string.IsNullOrEmpty(src)) return false;
            try
            {
                Uri uri;
                if (!Uri.TryCreate(src, UriKind.Absolute, out uri) &&
                    !Uri.TryCreate(src, UriKind.RelativeOrAbsolute, out uri))
                {
                    return false;
                }

                var brush = new ImageBrush
                {
                    ImageSource = new BitmapImage(uri),
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                BackgroundEllipse.Fill = brush;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private SolidColorBrush DeriveBackground()
        {
            long seed = ColorSeed;
            if (seed == 0)
            {
                string init = Initials ?? string.Empty;
                for (int i = 0; i < init.Length; i++) seed = seed * 31 + init[i];
            }
            int idx = (int)(((seed % Palette.Length) + Palette.Length) % Palette.Length);
            return new SolidColorBrush(Palette[idx]);
        }
    }
}
