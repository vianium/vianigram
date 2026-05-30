// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
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
            if (ImageEllipse != null)
            {
                ImageEllipse.Width = s;
                ImageEllipse.Height = s;
            }
            if (InitialsText != null)
            {
                InitialsText.FontSize = s * 0.38;
            }
        }

        private void ApplyVisual()
        {
            if (BackgroundEllipse == null || InitialsText == null) return;

            // Placeholder always renders — colour + initials. The real
            // avatar paints on top in the foreground tier and fades in
            // when it arrives, so we never see a blank-circle flash
            // between the row appearing and the bitmap landing.
            BackgroundEllipse.Fill = BackgroundColor != null ? BackgroundColor : DeriveBackground();
            InitialsText.Text = Initials ?? string.Empty;
            InitialsText.Visibility = Visibility.Visible;

            if (TryApplyImage())
            {
                BeginFadeIn();
            }
            else
            {
                // No image (yet); make sure the foreground tier is
                // invisible so the placeholder remains pristine.
                if (ImageEllipse != null)
                {
                    ImageEllipse.Fill = null;
                    ImageEllipse.Opacity = 0.0;
                }
            }
        }

        private bool TryApplyImage()
        {
            if (ImageEllipse == null) return false;

            // Prefer the directly-supplied BitmapImage (e.g. an expanded
            // stripped thumb or the HD JPEG fetched by PeerAvatarFetcher)
            // over the URL-style ImageSource so we can fill the avatar
            // without a network round-trip.
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
                    ImageEllipse.Fill = brush;
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
                ImageEllipse.Fill = brush;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BeginFadeIn()
        {
            if (ImageEllipse == null) return;

            // 200 ms opacity ramp 0 -> 1. Storyboard runs on the
            // composition thread (independent animation) so it costs
            // nothing on the UI thread once kicked off. Cancelling any
            // in-flight storyboard prevents stutter when the row is
            // rebound during scroll recycling.
            try
            {
                if (_fadeInStoryboard != null)
                {
                    try { _fadeInStoryboard.Stop(); } catch { }
                }
                ImageEllipse.Opacity = 0.0;
                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200))
                };
                Storyboard.SetTarget(anim, ImageEllipse);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);
                _fadeInStoryboard = sb;
                sb.Begin();
            }
            catch
            {
                // Storyboard plumbing is best-effort; if it fails we
                // just snap to full opacity rather than leaving the
                // avatar invisible.
                ImageEllipse.Opacity = 1.0;
            }
        }

        // Tracks the most recent fade-in so a fast successive image
        // assignment (stripped thumb -> HD JPEG) doesn't blend two
        // overlapping storyboards.
        private Storyboard _fadeInStoryboard;

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
