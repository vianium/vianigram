// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class TypingIndicator : UserControl
    {
        public static readonly DependencyProperty UserNameProperty =
            DependencyProperty.Register("UserName", typeof(string), typeof(TypingIndicator),
                new PropertyMetadata("", OnUserNameChanged));

        public string UserName
        {
            get { return (string)GetValue(UserNameProperty); }
            set { SetValue(UserNameProperty, value); }
        }

        public TypingIndicator()
        {
            this.InitializeComponent();
            ApplyName();
            this.Loaded += OnLoadedHandler;
            this.Unloaded += OnUnloadedHandler;
        }

        private static void OnUserNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var t = d as TypingIndicator;
            if (t != null) t.ApplyName();
        }

        private void ApplyName()
        {
            if (TypingText == null) return;
            string n = UserName;
            TypingText.Text = string.IsNullOrEmpty(n) ? "typing" : (n + " is typing");
        }

        private void OnLoadedHandler(object sender, RoutedEventArgs e)
        {
            if (DotsStoryboard != null)
            {
                DotsStoryboard.Begin();
            }
        }

        private void OnUnloadedHandler(object sender, RoutedEventArgs e)
        {
            if (DotsStoryboard != null)
            {
                DotsStoryboard.Stop();
            }
        }
    }
}
