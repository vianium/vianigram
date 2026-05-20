// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed partial class InfoBubble : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(InfoBubble),
                new PropertyMetadata("", OnTextChanged));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public InfoBubble()
        {
            this.InitializeComponent();
            ApplyText();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as InfoBubble;
            if (b != null) b.ApplyText();
        }

        private void ApplyText()
        {
            if (InfoText != null) InfoText.Text = Text ?? string.Empty;
        }
    }
}
