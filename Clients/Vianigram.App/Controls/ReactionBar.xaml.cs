// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed class ReactionView
    {
        public string Emoji { get; set; }
        public int Count { get; set; }
        public bool IsMine { get; set; }
    }

    public sealed partial class ReactionBar : UserControl
    {
        public static readonly DependencyProperty ReactionsProperty =
            DependencyProperty.Register("Reactions", typeof(IEnumerable), typeof(ReactionBar),
                new PropertyMetadata(null, OnReactionsChanged));

        public IEnumerable Reactions
        {
            get { return (IEnumerable)GetValue(ReactionsProperty); }
            set { SetValue(ReactionsProperty, value); }
        }

        public ReactionBar()
        {
            this.InitializeComponent();
            ApplyReactions();
        }

        private static void OnReactionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as ReactionBar;
            if (b != null) b.ApplyReactions();
        }

        private void ApplyReactions()
        {
            if (ReactionsItems != null) ReactionsItems.ItemsSource = Reactions;
        }
    }
}
