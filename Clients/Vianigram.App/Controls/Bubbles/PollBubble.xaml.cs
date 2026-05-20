// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed class PollVoteSelectedEventArgs : EventArgs
    {
        public int OptionIndex { get; private set; }
        public PollOptionView Option { get; private set; }

        public PollVoteSelectedEventArgs(int optionIndex, PollOptionView option)
        {
            OptionIndex = optionIndex;
            Option = option;
        }
    }

    public sealed class PollOptionView
    {
        public string Text { get; set; }
        public int Percent { get; set; }
        public int Votes { get; set; }
        public bool Voted { get; set; }

        public string PercentLabel
        {
            get { return Percent.ToString() + "%"; }
        }
    }

    public sealed partial class PollBubble : UserControl
    {
        public static readonly DependencyProperty QuestionProperty =
            DependencyProperty.Register("Question", typeof(string), typeof(PollBubble),
                new PropertyMetadata("", OnQuestionChanged));

        public static readonly DependencyProperty OptionsProperty =
            DependencyProperty.Register("Options", typeof(ObservableCollection<PollOptionView>), typeof(PollBubble),
                new PropertyMetadata(null, OnOptionsChanged));

        public static readonly DependencyProperty IsAnonymousProperty =
            DependencyProperty.Register("IsAnonymous", typeof(bool), typeof(PollBubble),
                new PropertyMetadata(false, OnLabelChanged));

        public static readonly DependencyProperty IsClosedProperty =
            DependencyProperty.Register("IsClosed", typeof(bool), typeof(PollBubble),
                new PropertyMetadata(false, OnLabelChanged));

        public static readonly DependencyProperty TotalVotersProperty =
            DependencyProperty.Register("TotalVoters", typeof(int), typeof(PollBubble),
                new PropertyMetadata(0, OnTotalChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(PollBubble),
                new PropertyMetadata(false, OnIsOutgoingChanged));

        public static readonly DependencyProperty CanVoteProperty =
            DependencyProperty.Register("CanVote", typeof(bool), typeof(PollBubble),
                new PropertyMetadata(true, OnOptionsVisualChanged));

        public static readonly DependencyProperty ShowResultsProperty =
            DependencyProperty.Register("ShowResults", typeof(bool), typeof(PollBubble),
                new PropertyMetadata(true, OnOptionsVisualChanged));

        public static readonly DependencyProperty VotedOptionIndexProperty =
            DependencyProperty.Register("VotedOptionIndex", typeof(int), typeof(PollBubble),
                new PropertyMetadata(-1, OnOptionsVisualChanged));

        public string Question
        {
            get { return (string)GetValue(QuestionProperty); }
            set { SetValue(QuestionProperty, value); }
        }

        public ObservableCollection<PollOptionView> Options
        {
            get { return (ObservableCollection<PollOptionView>)GetValue(OptionsProperty); }
            set { SetValue(OptionsProperty, value); }
        }

        public bool IsAnonymous
        {
            get { return (bool)GetValue(IsAnonymousProperty); }
            set { SetValue(IsAnonymousProperty, value); }
        }

        public bool IsClosed
        {
            get { return (bool)GetValue(IsClosedProperty); }
            set { SetValue(IsClosedProperty, value); }
        }

        public int TotalVoters
        {
            get { return (int)GetValue(TotalVotersProperty); }
            set { SetValue(TotalVotersProperty, value); }
        }

        public bool IsOutgoing
        {
            get { return (bool)GetValue(IsOutgoingProperty); }
            set { SetValue(IsOutgoingProperty, value); }
        }

        public bool CanVote
        {
            get { return (bool)GetValue(CanVoteProperty); }
            set { SetValue(CanVoteProperty, value); }
        }

        public bool ShowResults
        {
            get { return (bool)GetValue(ShowResultsProperty); }
            set { SetValue(ShowResultsProperty, value); }
        }

        public int VotedOptionIndex
        {
            get { return (int)GetValue(VotedOptionIndexProperty); }
            set { SetValue(VotedOptionIndexProperty, value); }
        }

        public event EventHandler<PollVoteSelectedEventArgs> VoteSelected;

        public PollBubble()
        {
            InitializeComponent();
            ApplyQuestion();
            ApplyOptions();
            ApplyLabel();
            ApplyTotal();
            ApplyAlignment();
        }

        private static void OnQuestionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null) b.ApplyQuestion();
        }

        private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null) b.ApplyOptions();
        }

        private static void OnOptionsVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null) b.ApplyOptions();
        }

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null) b.ApplyLabel();
        }

        private static void OnTotalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null)
            {
                b.ApplyTotal();
                b.ApplyOptions();
            }
        }

        private static void OnIsOutgoingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PollBubble b = d as PollBubble;
            if (b != null)
            {
                b.ApplyAlignment();
                b.ApplyOptions();
            }
        }

        private void ApplyQuestion()
        {
            if (QuestionText != null) QuestionText.Text = Question ?? string.Empty;
        }

        private void ApplyOptions()
        {
            if (OptionsPanel == null) return;

            OptionsPanel.Children.Clear();
            ObservableCollection<PollOptionView> options = Options;
            if (options == null || options.Count == 0) return;

            int total = TotalVoters > 0 ? TotalVoters : SumVotes(options);
            bool hasVote = VotedOptionIndex >= 0 || HasVotedOption(options);
            bool showResults = ShowResults || hasVote || IsClosed;

            for (int i = 0; i < options.Count; i++)
            {
                PollOptionView option = options[i];
                bool isSelected = i == VotedOptionIndex || (option != null && option.Voted);
                Grid optionGrid = CreateOptionGrid(option, i, total, showResults, isSelected);
                OptionsPanel.Children.Add(optionGrid);
            }
        }

        private Grid CreateOptionGrid(PollOptionView option, int index, int total, bool showResults, bool isSelected)
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 3, 0, 3);
            grid.MinHeight = 34;
            grid.Tag = index;
            grid.Background = new SolidColorBrush(Colors.Transparent);
            if (CanVote && !IsClosed && !showResults)
                grid.Tapped += OnOptionTapped;

            int percent = ResolvePercent(option, total);
            if (showResults)
            {
                Grid barHost = new Grid();
                barHost.Height = 34;
                barHost.HorizontalAlignment = HorizontalAlignment.Stretch;

                Rectangle background = new Rectangle();
                background.RadiusX = 4;
                background.RadiusY = 4;
                background.Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
                barHost.Children.Add(background);

                Rectangle bar = new Rectangle();
                bar.RadiusX = 4;
                bar.RadiusY = 4;
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                bar.Fill = isSelected
                    ? new SolidColorBrush(Color.FromArgb(80, 83, 189, 235))
                    : new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
                bar.Width = Math.Max(6, 2.7 * percent);
                barHost.Children.Add(bar);

                grid.Children.Add(barHost);
            }

            Grid content = new Grid();
            content.Margin = new Thickness(8, 7, 8, 7);
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            TextBlock text = new TextBlock();
            text.Text = option == null ? string.Empty : option.Text ?? string.Empty;
            text.FontSize = 14;
            text.TextWrapping = TextWrapping.Wrap;
            text.Foreground = new SolidColorBrush(Colors.White);
            content.Children.Add(text);

            if (showResults)
            {
                TextBlock pct = new TextBlock();
                pct.Text = percent.ToString() + "%";
                pct.FontSize = 12;
                pct.Margin = new Thickness(8, 0, 0, 0);
                pct.Opacity = 0.75;
                pct.Foreground = new SolidColorBrush(Colors.White);
                Grid.SetColumn(pct, 1);
                content.Children.Add(pct);
            }

            if (isSelected)
            {
                TextBlock check = new TextBlock();
                check.Text = "\u2713";
                check.FontSize = 13;
                check.Margin = new Thickness(6, 0, 0, 0);
                check.Foreground = new SolidColorBrush(Color.FromArgb(255, 83, 189, 235));
                Grid.SetColumn(check, 2);
                content.Children.Add(check);
            }

            grid.Children.Add(content);
            return grid;
        }

        private void ApplyLabel()
        {
            if (PollLabelText == null) return;

            string label = IsAnonymous ? "Anonymous Poll" : "Poll";
            if (IsClosed) label = label + " (closed)";
            PollLabelText.Text = label;
        }

        private void ApplyTotal()
        {
            if (TotalVotesText == null) return;

            int n = TotalVoters;
            if (n <= 0 && Options != null) n = SumVotes(Options);
            TotalVotesText.Text = n == 1 ? "1 voter" : n.ToString() + " voters";
        }

        private void ApplyAlignment()
        {
            if (RootGrid != null)
                RootGrid.HorizontalAlignment = IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void OnOptionTapped(object sender, TappedRoutedEventArgs e)
        {
            Grid grid = sender as Grid;
            if (grid == null || !(grid.Tag is int)) return;

            int index = (int)grid.Tag;
            ObservableCollection<PollOptionView> options = Options;
            if (options == null || index < 0 || index >= options.Count) return;

            VotedOptionIndex = index;
            options[index].Voted = true;
            options[index].Votes++;
            TotalVoters = Math.Max(TotalVoters + 1, SumVotes(options));
            ApplyOptions();

            EventHandler<PollVoteSelectedEventArgs> handler = VoteSelected;
            if (handler != null)
                handler(this, new PollVoteSelectedEventArgs(index, options[index]));
        }

        private static int ResolvePercent(PollOptionView option, int total)
        {
            if (option == null) return 0;
            if (option.Percent > 0) return Clamp(option.Percent, 0, 100);
            if (total <= 0) return 0;
            return Clamp((int)Math.Round((option.Votes * 100.0) / total), 0, 100);
        }

        private static int SumVotes(ObservableCollection<PollOptionView> options)
        {
            int total = 0;
            if (options == null) return total;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] != null) total += options[i].Votes;
            }
            return total;
        }

        private static bool HasVotedOption(ObservableCollection<PollOptionView> options)
        {
            if (options == null) return false;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null && options[i].Voted) return true;
            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
