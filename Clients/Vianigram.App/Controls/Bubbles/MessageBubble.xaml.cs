// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Vianigram.App.Controls.Bubbles
{
    public enum BubbleTextEntityType
    {
        Bold,
        Italic,
        Underline,
        Strikethrough,
        Code,
        Pre,
        Url,
        TextUrl,
        Mention,
        MentionName,
        Hashtag,
        Cashtag,
        BotCommand,
        Email,
        Phone,
        Spoiler,
        Blockquote
    }

    public sealed class BubbleTextEntity
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public BubbleTextEntityType Type { get; set; }
        public string Url { get; set; }
        public string Language { get; set; }
        public long UserId { get; set; }
    }

    public sealed partial class MessageBubble : UserControl
    {
        private static readonly Brush IncomingBubbleBrush = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35));
        private static readonly Brush OutgoingBubbleBrush = new SolidColorBrush(Color.FromArgb(255, 27, 161, 226));
        private static readonly Brush LightTextBrush = new SolidColorBrush(Colors.White);
        private static readonly Brush IncomingMetaBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
        private static readonly Brush OutgoingMetaBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        private static readonly Brush IncomingReplyStripeBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        private static readonly Brush OutgoingReplyStripeBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));

        private bool _revealSpoilers;
        private bool _hasSpoilers;

        public static readonly DependencyProperty MessageTextProperty =
            DependencyProperty.Register("MessageText", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty TimestampProperty =
            DependencyProperty.Register("Timestamp", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(MessageBubble),
                new PropertyMetadata(false, OnDirectionChanged));

        public static readonly DependencyProperty StatusGlyphProperty =
            DependencyProperty.Register("StatusGlyph", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty DeliveryStateProperty =
            DependencyProperty.Register("DeliveryState", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty HasReplyProperty =
            DependencyProperty.Register("HasReply", typeof(bool), typeof(MessageBubble),
                new PropertyMetadata(false, OnReplyChanged));

        public static readonly DependencyProperty ReplyAuthorProperty =
            DependencyProperty.Register("ReplyAuthor", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnReplyChanged));

        public static readonly DependencyProperty ReplyPreviewProperty =
            DependencyProperty.Register("ReplyPreview", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnReplyChanged));

        public static readonly DependencyProperty TextEntitiesProperty =
            DependencyProperty.Register("TextEntities", typeof(object), typeof(MessageBubble),
                new PropertyMetadata(null, OnTextChanged));

        public static readonly DependencyProperty RevealSpoilersProperty =
            DependencyProperty.Register("RevealSpoilers", typeof(bool), typeof(MessageBubble),
                new PropertyMetadata(false, OnRevealSpoilersChanged));

        public static readonly DependencyProperty ReactionSummaryProperty =
            DependencyProperty.Register("ReactionSummary", typeof(string), typeof(MessageBubble),
                new PropertyMetadata("", OnTextChanged));

        public string MessageText
        {
            get { return (string)GetValue(MessageTextProperty); }
            set { SetValue(MessageTextProperty, value); }
        }

        public string Timestamp
        {
            get { return (string)GetValue(TimestampProperty); }
            set { SetValue(TimestampProperty, value); }
        }

        public bool IsOutgoing
        {
            get { return (bool)GetValue(IsOutgoingProperty); }
            set { SetValue(IsOutgoingProperty, value); }
        }

        public string StatusGlyph
        {
            get { return (string)GetValue(StatusGlyphProperty); }
            set { SetValue(StatusGlyphProperty, value); }
        }

        public string DeliveryState
        {
            get { return (string)GetValue(DeliveryStateProperty); }
            set { SetValue(DeliveryStateProperty, value); }
        }

        public bool HasReply
        {
            get { return (bool)GetValue(HasReplyProperty); }
            set { SetValue(HasReplyProperty, value); }
        }

        public string ReplyAuthor
        {
            get { return (string)GetValue(ReplyAuthorProperty); }
            set { SetValue(ReplyAuthorProperty, value); }
        }

        public string ReplyPreview
        {
            get { return (string)GetValue(ReplyPreviewProperty); }
            set { SetValue(ReplyPreviewProperty, value); }
        }

        public object TextEntities
        {
            get { return GetValue(TextEntitiesProperty); }
            set { SetValue(TextEntitiesProperty, value); }
        }

        public bool RevealSpoilers
        {
            get { return (bool)GetValue(RevealSpoilersProperty); }
            set { SetValue(RevealSpoilersProperty, value); }
        }

        public string ReactionSummary
        {
            get { return (string)GetValue(ReactionSummaryProperty); }
            set { SetValue(ReactionSummaryProperty, value); }
        }

        public event EventHandler SpoilerRevealChanged;

        public MessageBubble()
        {
            InitializeComponent();
            BubbleInteractionHelpers.EnableTextSelection(MessageTextBlock);
            BubbleInteractionHelpers.EnableTextSelection(ReplyAuthorText);
            BubbleInteractionHelpers.EnableTextSelection(ReplyPreviewTextBlock);
            MessageTextBlock.Tapped += MessageTextBlock_Tapped;
            if (RootGrid != null)
            {
                RootGrid.Holding += OnBubbleHolding;
                RootGrid.RightTapped += OnBubbleRightTapped;
            }
            ApplyText();
            ApplyDirection();
            ApplyReply();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MessageBubble b = d as MessageBubble;
            if (b != null) b.ApplyText();
        }

        private static void OnRevealSpoilersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MessageBubble b = d as MessageBubble;
            if (b != null)
            {
                b._revealSpoilers = (bool)e.NewValue;
                b.ApplyText();
            }
        }

        private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MessageBubble b = d as MessageBubble;
            if (b != null)
            {
                b.ApplyDirection();
                b.ApplyText();
                b.ApplyReply();
            }
        }

        private static void OnReplyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MessageBubble b = d as MessageBubble;
            if (b != null) b.ApplyReply();
        }

        private void ApplyText()
        {
            if (MessageTextBlock != null)
            {
                _hasSpoilers = BubbleRichTextRenderer.Render(
                    MessageTextBlock,
                    MessageText ?? string.Empty,
                    TextEntities,
                    _revealSpoilers);
            }

            if (TimeText != null) TimeText.Text = Timestamp ?? string.Empty;

            if (ReactionsText != null)
            {
                string reactions = ReactionSummary ?? string.Empty;
                ReactionsText.Text = reactions;
                ReactionsText.Visibility = string.IsNullOrEmpty(reactions)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            string glyph = StatusGlyph;
            if (string.IsNullOrEmpty(glyph))
                glyph = MapDeliveryState(DeliveryState);

            if (StatusText != null)
            {
                bool show = IsOutgoing && !string.IsNullOrEmpty(glyph);
                StatusText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                StatusText.Text = glyph ?? string.Empty;
            }
        }

        private void ApplyDirection()
        {
            bool outgoing = IsOutgoing;
            if (RootGrid != null)
                RootGrid.Margin = outgoing ? new Thickness(86, 3, 2, 3) : new Thickness(2, 3, 86, 3);
            if (BubbleBorder != null) BubbleBorder.HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            Brush bubbleBrush = outgoing ? OutgoingBubbleBrush : IncomingBubbleBrush;
            Brush metaBrush = outgoing ? OutgoingMetaBrush : IncomingMetaBrush;

            if (BubbleBorder != null) BubbleBorder.Background = bubbleBrush;
            if (MessageTextBlock != null) MessageTextBlock.Foreground = LightTextBrush;
            if (TimeText != null) TimeText.Foreground = metaBrush;
            if (StatusText != null) StatusText.Foreground = metaBrush;
            if (ReplyPreviewTextBlock != null) ReplyPreviewTextBlock.Foreground = metaBrush;
        }

        private void ApplyReply()
        {
            bool show = HasReply;
            if (ReplyHost != null)
            {
                ReplyHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ReplyHost.BorderBrush = IsOutgoing ? OutgoingReplyStripeBrush : IncomingReplyStripeBrush;
            }

            if (ReplyAuthorText != null) ReplyAuthorText.Text = ReplyAuthor ?? string.Empty;
            if (ReplyPreviewTextBlock != null) ReplyPreviewTextBlock.Text = ReplyPreview ?? string.Empty;
        }

        private void MessageTextBlock_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_hasSpoilers) return;

            _revealSpoilers = !_revealSpoilers;
            SetValue(RevealSpoilersProperty, _revealSpoilers);
            ApplyText();

            EventHandler handler = SpoilerRevealChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnBubbleHolding(object sender, HoldingRoutedEventArgs e)
        {
            if (e == null || e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            if (BubbleInteractionHelpers.IsFrom(MessageTextBlock, e.OriginalSource) ||
                BubbleInteractionHelpers.IsFrom(ReplyAuthorText, e.OriginalSource) ||
                BubbleInteractionHelpers.IsFrom(ReplyPreviewTextBlock, e.OriginalSource))
                return;

            ShowCopyMenu();
            e.Handled = true;
        }

        private void OnBubbleRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ShowCopyMenu();
            if (e != null) e.Handled = true;
        }

        private void ShowCopyMenu()
        {
            string text = MessageText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return;

            BubbleInteractionHelpers.ShowCopyTextFlyout(
                BubbleBorder != null ? (FrameworkElement)BubbleBorder : this,
                text,
                "Copy message");
        }

        private static string MapDeliveryState(string state)
        {
            switch (state ?? string.Empty)
            {
                case "Sending": return "...";
                case "Sent": return "\u2713";
                case "Delivered": return "\u2713\u2713";
                case "Read": return "\u2713\u2713";
                case "Failed": return "!";
                default: return string.Empty;
            }
        }
    }

    internal static class BubbleInteractionHelpers
    {
        private static bool _clipboardResolved;
        private static MethodInfo _setClipboardContent;

        public static void EnableTextSelection(TextBlock textBlock)
        {
            if (textBlock == null) return;

            try
            {
                PropertyInfo property = textBlock.GetType().GetRuntimeProperty("IsTextSelectionEnabled");
                if (property != null && property.CanWrite)
                    property.SetValue(textBlock, true);
            }
            catch
            {
            }
        }

        public static bool CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            try
            {
                MethodInfo setContent = ResolveClipboardSetContent();
                if (setContent == null) return false;

                var package = new DataPackage();
                package.SetText(text);
                setContent.Invoke(null, new object[] { package });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ShowCopyTextFlyout(FrameworkElement anchor, string text, string label)
        {
            if (anchor == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                var flyout = new MenuFlyout();
                var copy = new MenuFlyoutItem { Text = string.IsNullOrEmpty(label) ? "Copy" : label };
                copy.Click += delegate { CopyText(text); };
                flyout.Items.Add(copy);
                flyout.ShowAt(anchor);
            }
            catch
            {
                CopyText(text);
            }
        }

        public static bool IsFrom(DependencyObject root, object source)
        {
            if (root == null || source == null) return false;

            DependencyObject current = source as DependencyObject;
            while (current != null)
            {
                if (ReferenceEquals(current, root)) return true;
                try { current = VisualTreeHelper.GetParent(current); }
                catch { return false; }
            }

            return false;
        }

        private static MethodInfo ResolveClipboardSetContent()
        {
            if (_clipboardResolved) return _setClipboardContent;
            _clipboardResolved = true;

            try
            {
                Type clipboardType = Type.GetType(
                    "Windows.ApplicationModel.DataTransfer.Clipboard, Windows, ContentType=WindowsRuntime");
                if (clipboardType == null) return null;

                foreach (MethodInfo method in clipboardType.GetTypeInfo().DeclaredMethods)
                {
                    if (method != null && method.Name == "SetContent")
                    {
                        _setClipboardContent = method;
                        break;
                    }
                }
            }
            catch
            {
                _setClipboardContent = null;
            }

            return _setClipboardContent;
        }
    }

    internal static class BubbleRichTextRenderer
    {
        private static readonly Regex UrlRegex = new Regex(@"((https?://|www\.)[^\s<>()]+)", RegexOptions.IgnoreCase);

        public static bool Render(TextBlock target, string text, object sourceEntities, bool revealSpoilers)
        {
            if (target == null) return false;

            BubbleRichPreparedText prepared = PrepareText(text, sourceEntities);
            List<BubbleTextEntity> entities = NormalizeEntities(prepared.Entities, prepared.Text.Length);
            bool hasSpoiler = ContainsEntityType(entities, BubbleTextEntityType.Spoiler);

            target.Text = string.Empty;
            target.Inlines.Clear();

            if (string.IsNullOrEmpty(prepared.Text)) return hasSpoiler;

            if (entities.Count == 0)
            {
                target.Text = prepared.Text;
                return false;
            }

            List<int> points = BuildSegmentPoints(prepared.Text.Length, entities);
            for (int i = 0; i < points.Count - 1; i++)
            {
                int start = points[i];
                int end = points[i + 1];
                if (end <= start) continue;

                string segment = prepared.Text.Substring(start, end - start);
                List<BubbleTextEntity> active = GetActiveEntities(entities, start, end);
                AddSegment(target, segment, start, active, revealSpoilers);
            }

            return hasSpoiler;
        }

        private static BubbleRichPreparedText PrepareText(string text, object sourceEntities)
        {
            BubbleRichPreparedText prepared = new BubbleRichPreparedText();
            prepared.Text = text ?? string.Empty;
            prepared.Entities = ReadEntities(sourceEntities);

            if (prepared.Entities.Count > 0) return prepared;

            ParseFallbackMarkup(prepared);
            AddAutoLinks(prepared);
            return prepared;
        }

        private static List<BubbleTextEntity> ReadEntities(object source)
        {
            List<BubbleTextEntity> result = new List<BubbleTextEntity>();
            IEnumerable enumerable = source as IEnumerable;
            if (enumerable == null || source is string) return result;

            foreach (object item in enumerable)
            {
                BubbleTextEntity entity = ReadEntity(item);
                if (entity != null) result.Add(entity);
            }

            return result;
        }

        private static BubbleTextEntity ReadEntity(object item)
        {
            BubbleTextEntity direct = item as BubbleTextEntity;
            if (direct != null) return CloneEntity(direct);
            if (item == null) return null;

            BubbleTextEntity entity = new BubbleTextEntity();
            entity.Offset = ReadInt(item, "Offset");
            entity.Length = ReadInt(item, "Length");
            entity.Url = ReadString(item, "Url");
            entity.Language = ReadString(item, "Language");
            entity.UserId = ReadLong(item, "UserId");

            string type = ReadString(item, "Type");
            if (string.IsNullOrEmpty(type)) type = ReadString(item, "Kind");
            BubbleTextEntityType parsed;
            try
            {
                parsed = (BubbleTextEntityType)Enum.Parse(typeof(BubbleTextEntityType), type ?? string.Empty, true);
            }
            catch
            {
                return null;
            }

            entity.Type = parsed;
            return entity;
        }

        private static int ReadInt(object source, string propertyName)
        {
            object value = ReadProperty(source, propertyName);
            if (value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static long ReadLong(object source, string propertyName)
        {
            object value = ReadProperty(source, propertyName);
            if (value == null) return 0;
            try { return Convert.ToInt64(value); }
            catch { return 0; }
        }

        private static string ReadString(object source, string propertyName)
        {
            object value = ReadProperty(source, propertyName);
            return value == null ? null : value.ToString();
        }

        private static object ReadProperty(object source, string propertyName)
        {
            PropertyInfo prop = source.GetType().GetRuntimeProperty(propertyName);
            return prop == null ? null : prop.GetValue(source);
        }

        private static void ParseFallbackMarkup(BubbleRichPreparedText prepared)
        {
            string source = prepared.Text;
            if (string.IsNullOrEmpty(source)) return;

            StringBuilder output = new StringBuilder(source.Length);
            List<BubbleTextEntity> entities = new List<BubbleTextEntity>();

            for (int i = 0; i < source.Length;)
            {
                if (TryReadDelimited(source, ref i, output, entities, "**", BubbleTextEntityType.Bold)) continue;
                if (TryReadDelimited(source, ref i, output, entities, "__", BubbleTextEntityType.Underline)) continue;
                if (TryReadDelimited(source, ref i, output, entities, "~~", BubbleTextEntityType.Strikethrough)) continue;
                if (TryReadDelimited(source, ref i, output, entities, "||", BubbleTextEntityType.Spoiler)) continue;
                if (TryReadDelimited(source, ref i, output, entities, "`", BubbleTextEntityType.Code)) continue;
                if (TryReadDelimited(source, ref i, output, entities, "_", BubbleTextEntityType.Italic)) continue;
                if (TryReadMarkdownLink(source, ref i, output, entities)) continue;

                output.Append(source[i]);
                i++;
            }

            prepared.Text = output.ToString();
            prepared.Entities = entities;
        }

        private static bool TryReadDelimited(string source, ref int index, StringBuilder output,
            List<BubbleTextEntity> entities, string delimiter, BubbleTextEntityType type)
        {
            if (!StartsWith(source, index, delimiter)) return false;

            int contentStart = index + delimiter.Length;
            int close = source.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
            if (close <= contentStart) return false;

            string content = source.Substring(contentStart, close - contentStart);
            int offset = output.Length;
            output.Append(content);
            entities.Add(new BubbleTextEntity { Offset = offset, Length = content.Length, Type = type });
            index = close + delimiter.Length;
            return true;
        }

        private static bool TryReadMarkdownLink(string source, ref int index, StringBuilder output,
            List<BubbleTextEntity> entities)
        {
            if (source[index] != '[') return false;

            int labelEnd = source.IndexOf("](", index, StringComparison.Ordinal);
            if (labelEnd <= index + 1) return false;

            int urlStart = labelEnd + 2;
            int urlEnd = source.IndexOf(')', urlStart);
            if (urlEnd <= urlStart) return false;

            string label = source.Substring(index + 1, labelEnd - index - 1);
            string url = source.Substring(urlStart, urlEnd - urlStart);
            int offset = output.Length;
            output.Append(label);
            entities.Add(new BubbleTextEntity
            {
                Offset = offset,
                Length = label.Length,
                Type = BubbleTextEntityType.TextUrl,
                Url = url
            });

            index = urlEnd + 1;
            return true;
        }

        private static void AddAutoLinks(BubbleRichPreparedText prepared)
        {
            MatchCollection matches = UrlRegex.Matches(prepared.Text);
            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                if (!match.Success || match.Length <= 0 ||
                    IsCoveredByEntity(prepared.Entities, match.Index, match.Index + match.Length))
                    continue;

                prepared.Entities.Add(new BubbleTextEntity
                {
                    Offset = match.Index,
                    Length = match.Length,
                    Type = BubbleTextEntityType.Url
                });
            }
        }

        private static List<BubbleTextEntity> NormalizeEntities(IList<BubbleTextEntity> source, int textLength)
        {
            List<BubbleTextEntity> result = new List<BubbleTextEntity>();
            if (source == null || textLength <= 0) return result;

            for (int i = 0; i < source.Count; i++)
            {
                BubbleTextEntity entity = source[i];
                if (entity == null || entity.Length <= 0 || entity.Offset >= textLength) continue;

                int offset = Math.Max(0, entity.Offset);
                int length = Math.Min(entity.Length, textLength - offset);
                if (length <= 0) continue;

                BubbleTextEntity clone = CloneEntity(entity);
                clone.Offset = offset;
                clone.Length = length;
                result.Add(clone);
            }

            result.Sort(delegate(BubbleTextEntity left, BubbleTextEntity right)
            {
                int cmp = left.Offset.CompareTo(right.Offset);
                if (cmp != 0) return cmp;
                return right.Length.CompareTo(left.Length);
            });

            return result;
        }

        private static List<int> BuildSegmentPoints(int textLength, List<BubbleTextEntity> entities)
        {
            List<int> points = new List<int>();
            AddPoint(points, 0);
            AddPoint(points, textLength);

            for (int i = 0; i < entities.Count; i++)
            {
                AddPoint(points, entities[i].Offset);
                AddPoint(points, entities[i].Offset + entities[i].Length);
            }

            points.Sort();
            return points;
        }

        private static void AddPoint(List<int> points, int point)
        {
            if (point < 0) return;
            for (int i = 0; i < points.Count; i++)
                if (points[i] == point) return;
            points.Add(point);
        }

        private static List<BubbleTextEntity> GetActiveEntities(List<BubbleTextEntity> entities, int start, int end)
        {
            List<BubbleTextEntity> active = new List<BubbleTextEntity>();
            for (int i = 0; i < entities.Count; i++)
            {
                BubbleTextEntity entity = entities[i];
                int entityEnd = entity.Offset + entity.Length;
                if (entity.Offset <= start && entityEnd >= end) active.Add(entity);
            }

            return active;
        }

        private static void AddSegment(TextBlock target, string segment, int segmentStart,
            List<BubbleTextEntity> active, bool revealSpoilers)
        {
            bool hiddenSpoiler = ContainsEntityType(active, BubbleTextEntityType.Spoiler) && !revealSpoilers;
            bool blockquoteStart = IsEntityStart(active, BubbleTextEntityType.Blockquote, segmentStart);
            string display = hiddenSpoiler ? MakeSpoilerMask(segment) : segment;
            if (blockquoteStart) display = "\u2502 " + display;

            Run run = new Run();
            run.Text = display;
            ApplyRunStyle(run, active, hiddenSpoiler);

            Inline inline = run;
            if (ContainsEntityType(active, BubbleTextEntityType.Underline))
            {
                Underline underline = new Underline();
                underline.Inlines.Add(run);
                inline = underline;
            }

            string linkUrl = ResolveLinkUrl(segment, active);
            if (!string.IsNullOrEmpty(linkUrl))
            {
                Hyperlink link = new Hyperlink();
                link.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 180, 235));
                try { link.NavigateUri = new Uri(linkUrl); }
                catch { }

                link.Inlines.Add(inline);
                target.Inlines.Add(link);
                return;
            }

            target.Inlines.Add(inline);
        }

        private static void ApplyRunStyle(Run run, List<BubbleTextEntity> active, bool hiddenSpoiler)
        {
            if (ContainsEntityType(active, BubbleTextEntityType.Bold))
                run.FontWeight = FontWeights.SemiBold;

            if (ContainsEntityType(active, BubbleTextEntityType.Italic))
                run.FontStyle = FontStyle.Italic;

            if (ContainsEntityType(active, BubbleTextEntityType.Code) ||
                ContainsEntityType(active, BubbleTextEntityType.Pre))
            {
                run.FontFamily = new FontFamily("Consolas");
                run.Foreground = new SolidColorBrush(Color.FromArgb(255, 210, 225, 235));
            }
            else if (hiddenSpoiler)
            {
                run.Foreground = new SolidColorBrush(Color.FromArgb(255, 115, 125, 135));
            }
            else if (ContainsEntityType(active, BubbleTextEntityType.Url) ||
                     ContainsEntityType(active, BubbleTextEntityType.TextUrl) ||
                     ContainsEntityType(active, BubbleTextEntityType.Mention) ||
                     ContainsEntityType(active, BubbleTextEntityType.MentionName) ||
                     ContainsEntityType(active, BubbleTextEntityType.Hashtag) ||
                     ContainsEntityType(active, BubbleTextEntityType.Cashtag) ||
                     ContainsEntityType(active, BubbleTextEntityType.BotCommand) ||
                     ContainsEntityType(active, BubbleTextEntityType.Email) ||
                     ContainsEntityType(active, BubbleTextEntityType.Phone))
            {
                run.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 180, 235));
            }
            else if (ContainsEntityType(active, BubbleTextEntityType.Blockquote))
            {
                run.Foreground = new SolidColorBrush(Color.FromArgb(255, 185, 198, 205));
            }
            else if (ContainsEntityType(active, BubbleTextEntityType.Strikethrough))
            {
                run.Foreground = new SolidColorBrush(Color.FromArgb(190, 180, 190, 198));
            }
        }

        private static string ResolveLinkUrl(string segment, List<BubbleTextEntity> active)
        {
            for (int i = 0; i < active.Count; i++)
            {
                BubbleTextEntity entity = active[i];
                if (entity.Type == BubbleTextEntityType.TextUrl && !string.IsNullOrEmpty(entity.Url))
                    return NormalizeUrl(entity.Url);

                if (entity.Type == BubbleTextEntityType.Url)
                    return NormalizeUrl(segment);

                if (entity.Type == BubbleTextEntityType.Email)
                    return "mailto:" + segment;

                if (entity.Type == BubbleTextEntityType.Phone)
                    return "tel:" + segment;
            }

            return null;
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("tg://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                return url;

            return "http://" + url;
        }

        private static bool ContainsEntityType(IList<BubbleTextEntity> entities, BubbleTextEntityType type)
        {
            if (entities == null) return false;
            for (int i = 0; i < entities.Count; i++)
                if (entities[i] != null && entities[i].Type == type) return true;
            return false;
        }

        private static bool IsEntityStart(IList<BubbleTextEntity> entities, BubbleTextEntityType type, int start)
        {
            if (entities == null) return false;
            for (int i = 0; i < entities.Count; i++)
                if (entities[i] != null && entities[i].Type == type && entities[i].Offset == start) return true;
            return false;
        }

        private static bool IsCoveredByEntity(IList<BubbleTextEntity> entities, int start, int end)
        {
            if (entities == null) return false;
            for (int i = 0; i < entities.Count; i++)
            {
                BubbleTextEntity entity = entities[i];
                if (entity == null) continue;
                int entityEnd = entity.Offset + entity.Length;
                if (entity.Offset <= start && entityEnd >= end) return true;
            }

            return false;
        }

        private static string MakeSpoilerMask(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
                builder.Append(char.IsWhiteSpace(text[i]) ? text[i] : '\u25CF');

            return builder.ToString();
        }

        private static bool StartsWith(string value, int index, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token) || index + token.Length > value.Length)
                return false;

            for (int i = 0; i < token.Length; i++)
                if (value[index + i] != token[i]) return false;

            return true;
        }

        private static BubbleTextEntity CloneEntity(BubbleTextEntity source)
        {
            if (source == null) return null;

            return new BubbleTextEntity
            {
                Offset = source.Offset,
                Length = source.Length,
                Type = source.Type,
                Url = source.Url,
                Language = source.Language,
                UserId = source.UserId
            };
        }

        private sealed class BubbleRichPreparedText
        {
            public string Text { get; set; }
            public List<BubbleTextEntity> Entities { get; set; }
        }
    }
}
