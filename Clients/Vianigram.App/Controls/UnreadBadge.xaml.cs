// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Vianigram.App.Controls
{
    public sealed partial class UnreadBadge : UserControl
    {
        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(int), typeof(UnreadBadge),
                new PropertyMetadata(0, OnVisualChanged));

        public static readonly DependencyProperty IsMentionProperty =
            DependencyProperty.Register("IsMention", typeof(bool), typeof(UnreadBadge),
                new PropertyMetadata(false, OnVisualChanged));

        public int Count
        {
            get { return (int)GetValue(CountProperty); }
            set { SetValue(CountProperty, value); }
        }

        public bool IsMention
        {
            get { return (bool)GetValue(IsMentionProperty); }
            set { SetValue(IsMentionProperty, value); }
        }

        public UnreadBadge()
        {
            this.InitializeComponent();
            ApplyVisual();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as UnreadBadge;
            if (b != null) b.ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (RootGrid == null || BadgeBorder == null || CountText == null) return;
            int n = Count;
            if (n <= 0)
            {
                RootGrid.Visibility = Visibility.Collapsed;
                return;
            }

            RootGrid.Visibility = Visibility.Visible;
            CountText.Text = n > 999 ? "999+" : n.ToString();

            if (IsMention)
            {
                BadgeBorder.Background = new SolidColorBrush(Color.FromArgb(255, 230, 95, 92));
            }
            else
            {
                object res;
                if (Application.Current.Resources.TryGetValue("VgAccentBrush", out res) &&
                    res is Brush)
                {
                    BadgeBorder.Background = (Brush)res;
                }
                else
                {
                    BadgeBorder.Background = new SolidColorBrush(Color.FromArgb(255, 83, 189, 235));
                }
            }
        }
    }
}
