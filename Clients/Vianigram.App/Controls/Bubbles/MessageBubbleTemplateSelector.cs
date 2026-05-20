// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MessageBubbleTemplateSelector.cs
//
// Routes a MessageRow to the correct DataTemplate based on its
// MessageRowKind discriminator. Each kind maps to a property on the
// selector that ChatPage.xaml fills with a <DataTemplate> resource.
// Falls back to TextTemplate when a kind hasn't been wired up — keeps
// the list rendering even if a future variant lands without UI work.

using Vianigram.App.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed class MessageBubbleTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }
        public DataTemplate PhotoTemplate { get; set; }
        public DataTemplate VoiceTemplate { get; set; }
        public DataTemplate AudioTemplate { get; set; }
        public DataTemplate VideoTemplate { get; set; }
        public DataTemplate VideoNoteTemplate { get; set; }
        public DataTemplate AnimationTemplate { get; set; }
        public DataTemplate DocumentTemplate { get; set; }
        public DataTemplate StickerTemplate { get; set; }
        public DataTemplate ContactTemplate { get; set; }
        public DataTemplate LocationTemplate { get; set; }
        public DataTemplate PollTemplate { get; set; }
        public DataTemplate WebPageTemplate { get; set; }
        public DataTemplate ServiceTemplate { get; set; }
        public DataTemplate UnsupportedTemplate { get; set; }
        public DataTemplate DaySeparatorTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            var row = item as MessageRow;
            if (row == null) return TextTemplate;
            switch (row.Kind)
            {
                case MessageRowKind.Photo:        return PhotoTemplate ?? TextTemplate;
                case MessageRowKind.Voice:        return VoiceTemplate ?? TextTemplate;
                case MessageRowKind.Audio:        return AudioTemplate ?? VoiceTemplate ?? TextTemplate;
                case MessageRowKind.Video:        return VideoTemplate ?? PhotoTemplate ?? TextTemplate;
                case MessageRowKind.VideoNote:    return VideoNoteTemplate ?? VideoTemplate ?? PhotoTemplate ?? TextTemplate;
                case MessageRowKind.Animation:    return AnimationTemplate ?? VideoTemplate ?? PhotoTemplate ?? TextTemplate;
                case MessageRowKind.Document:     return DocumentTemplate ?? TextTemplate;
                case MessageRowKind.Sticker:      return StickerTemplate ?? TextTemplate;
                case MessageRowKind.Contact:      return ContactTemplate ?? TextTemplate;
                case MessageRowKind.Location:     return LocationTemplate ?? TextTemplate;
                case MessageRowKind.Poll:         return PollTemplate ?? TextTemplate;
                case MessageRowKind.WebPage:      return WebPageTemplate ?? TextTemplate;
                case MessageRowKind.Service:      return ServiceTemplate ?? TextTemplate;
                case MessageRowKind.Unsupported:  return UnsupportedTemplate ?? TextTemplate;
                case MessageRowKind.DaySeparator: return DaySeparatorTemplate ?? ServiceTemplate ?? TextTemplate;
                default:                          return TextTemplate;
            }
        }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return SelectTemplateCore(item, null);
        }
    }
}
