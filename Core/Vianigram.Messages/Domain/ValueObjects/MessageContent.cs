// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Messages.Domain.ValueObjects
{
    public sealed class TelegramMediaFile
    {
        private static readonly byte[] EmptyBytes = new byte[0];

        public TelegramMediaFile(string fileId, long accessHash, byte[] fileReference,
            int dcId, long size, string mimeType, string fileName,
            string localPath, string localFullPath)
        {
            FileId = fileId ?? string.Empty;
            AccessHash = accessHash;
            FileReference = CopyBytes(fileReference);
            DcId = dcId;
            Size = size;
            MimeType = mimeType ?? string.Empty;
            FileName = fileName ?? string.Empty;
            LocalPath = localPath ?? string.Empty;
            LocalFullPath = localFullPath ?? string.Empty;
        }

        public string FileId { get; private set; }
        public long AccessHash { get; private set; }
        public byte[] FileReference { get; private set; }
        public int DcId { get; private set; }
        public long Size { get; private set; }
        public string MimeType { get; private set; }
        public string FileName { get; private set; }
        public string LocalPath { get; private set; }
        public string LocalFullPath { get; private set; }

        private static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return EmptyBytes;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    public sealed class MediaThumbnail
    {
        private static readonly byte[] EmptyBytes = new byte[0];

        public MediaThumbnail(string sizeType, int width, int height, long size,
            string localPath, string url, byte[] bytes)
        {
            SizeType = sizeType ?? string.Empty;
            Width = width;
            Height = height;
            Size = size;
            LocalPath = localPath ?? string.Empty;
            Url = url ?? string.Empty;
            Bytes = CopyBytes(bytes);
        }

        public string SizeType { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long Size { get; private set; }
        public string LocalPath { get; private set; }
        public string Url { get; private set; }
        public byte[] Bytes { get; private set; }

        private static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return EmptyBytes;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    public sealed class PollOption
    {
        private static readonly byte[] EmptyBytes = new byte[0];

        public PollOption(string text, byte[] token, int voteCount,
            bool isChosen, bool isCorrect)
        {
            Text = text ?? string.Empty;
            Token = CopyBytes(token);
            VoteCount = voteCount;
            IsChosen = isChosen;
            IsCorrect = isCorrect;
        }

        public string Text { get; private set; }
        public byte[] Token { get; private set; }
        public int VoteCount { get; private set; }
        public bool IsChosen { get; private set; }
        public bool IsCorrect { get; private set; }

        private static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return EmptyBytes;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    public sealed class MessageReaction
    {
        public MessageReaction(string emoticon, string customEmojiId, int count, bool isChosen)
        {
            Emoticon = emoticon ?? string.Empty;
            CustomEmojiId = customEmojiId ?? string.Empty;
            Count = count;
            IsChosen = isChosen;
        }

        public string Emoticon { get; private set; }
        public string CustomEmojiId { get; private set; }
        public int Count { get; private set; }
        public bool IsChosen { get; private set; }
    }

    public sealed class MessageInlineButton
    {
        public MessageInlineButton(string text, string url, string callbackData,
            string switchInlineQuery, bool requiresPassword)
        {
            Text = text ?? string.Empty;
            Url = url ?? string.Empty;
            CallbackData = callbackData ?? string.Empty;
            SwitchInlineQuery = switchInlineQuery ?? string.Empty;
            RequiresPassword = requiresPassword;
        }

        public string Text { get; private set; }
        public string Url { get; private set; }
        public string CallbackData { get; private set; }
        public string SwitchInlineQuery { get; private set; }
        public bool RequiresPassword { get; private set; }
    }

    public sealed class MessageInlineButtonRow
    {
        private static readonly MessageInlineButton[] EmptyButtons = new MessageInlineButton[0];

        public MessageInlineButtonRow(IList<MessageInlineButton> buttons)
        {
            Buttons = buttons != null ? CopyToReadOnly(buttons) : EmptyButtons;
        }

        public IList<MessageInlineButton> Buttons { get; private set; }

        private static MessageInlineButton[] CopyToReadOnly(IList<MessageInlineButton> source)
        {
            var arr = new MessageInlineButton[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    /// <summary>
    /// Polymorphic message body. Concrete subclasses are sealed and immutable.
    /// New variants are added by extending the closed sum here; consumers should
    /// dispatch by runtime type.
    /// </summary>
    public abstract class MessageContent
    {
        private static readonly MessageReaction[] EmptyReactions = new MessageReaction[0];
        private static readonly MessageInlineButtonRow[] EmptyButtonRows = new MessageInlineButtonRow[0];

        protected MessageContent()
        {
            UnsupportedSummary = string.Empty;
            Reactions = EmptyReactions;
            ButtonRows = EmptyButtonRows;
        }

        public uint RawConstructorId { get; protected set; }
        public string UnsupportedSummary { get; protected set; }
        public IList<MessageReaction> Reactions { get; protected set; }
        public IList<MessageInlineButtonRow> ButtonRows { get; protected set; }

        protected void SetTelegramMetadata(uint rawConstructorId, string unsupportedSummary,
            IList<MessageReaction> reactions, IList<MessageInlineButtonRow> buttonRows)
        {
            RawConstructorId = rawConstructorId;
            UnsupportedSummary = unsupportedSummary ?? string.Empty;
            Reactions = reactions != null ? CopyReactions(reactions) : EmptyReactions;
            ButtonRows = buttonRows != null ? CopyButtonRows(buttonRows) : EmptyButtonRows;
        }

        private static MessageReaction[] CopyReactions(IList<MessageReaction> source)
        {
            var arr = new MessageReaction[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static MessageInlineButtonRow[] CopyButtonRows(IList<MessageInlineButtonRow> source)
        {
            var arr = new MessageInlineButtonRow[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public sealed class MessageContentText : MessageContent
    {
        private static readonly MessageEntity[] EmptyEntities = new MessageEntity[0];

        public MessageContentText(string body, IList<MessageEntity> entities = null)
            : this(body, entities, 0, null, null, null)
        {
        }

        public MessageContentText(string body, IList<MessageEntity> entities,
            uint rawConstructorId, IList<MessageReaction> reactions,
            IList<MessageInlineButtonRow> buttonRows)
            : this(body, entities, rawConstructorId, null, reactions, buttonRows)
        {
        }

        public MessageContentText(string body, IList<MessageEntity> entities,
            uint rawConstructorId, string unsupportedSummary,
            IList<MessageReaction> reactions, IList<MessageInlineButtonRow> buttonRows)
        {
            Body = body ?? string.Empty;
            Entities = entities != null ? CopyToReadOnly(entities) : EmptyEntities;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public string Body { get; private set; }
        public IList<MessageEntity> Entities { get; private set; }

        private static MessageEntity[] CopyToReadOnly(IList<MessageEntity> source)
        {
            var arr = new MessageEntity[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public sealed class MessageContentPhoto : MessageContent
    {
        public MessageContentPhoto(string localThumbPath, string localFullPath, int width, int height, string caption)
            : this(localThumbPath, localFullPath, width, height, caption, null, null, null, 0, null, null, null)
        {
        }

        public MessageContentPhoto(string localThumbPath, string localFullPath, int width, int height,
            string caption, IList<MessageEntity> captionEntities, TelegramMediaFile file,
            IList<MediaThumbnail> thumbnails, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            LocalThumbPath = localThumbPath ?? string.Empty;
            LocalFullPath = localFullPath ?? string.Empty;
            Width = width;
            Height = height;
            Caption = caption ?? string.Empty;
            CaptionEntities = captionEntities != null ? CopyEntities(captionEntities) : EmptyEntities;
            File = file;
            Thumbnails = thumbnails != null ? CopyThumbnails(thumbnails) : EmptyThumbnails;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly MessageEntity[] EmptyEntities = new MessageEntity[0];
        private static readonly MediaThumbnail[] EmptyThumbnails = new MediaThumbnail[0];

        public string LocalThumbPath { get; private set; }
        public string LocalFullPath { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Caption { get; private set; }
        public IList<MessageEntity> CaptionEntities { get; private set; }
        public TelegramMediaFile File { get; private set; }
        public IList<MediaThumbnail> Thumbnails { get; private set; }

        private static MessageEntity[] CopyEntities(IList<MessageEntity> source)
        {
            var arr = new MessageEntity[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static MediaThumbnail[] CopyThumbnails(IList<MediaThumbnail> source)
        {
            var arr = new MediaThumbnail[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public sealed class MessageContentDocument : MessageContent
    {
        public MessageContentDocument(string fileName, long size, string mimeType, string localPath, string caption)
            : this(fileName, size, mimeType, localPath, localPath, caption, null, null, null, 0, null, null, null)
        {
        }

        public MessageContentDocument(string fileName, long size, string mimeType,
            string localPath, string localFullPath, string caption,
            IList<MessageEntity> captionEntities, TelegramMediaFile file,
            IList<MediaThumbnail> thumbnails, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            FileName = fileName ?? string.Empty;
            Size = size;
            MimeType = mimeType ?? string.Empty;
            LocalPath = localPath ?? string.Empty;
            LocalFullPath = localFullPath ?? string.Empty;
            Caption = caption ?? string.Empty;
            CaptionEntities = captionEntities != null ? CopyEntities(captionEntities) : EmptyEntities;
            File = file;
            Thumbnails = thumbnails != null ? CopyThumbnails(thumbnails) : EmptyThumbnails;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly MessageEntity[] EmptyEntities = new MessageEntity[0];
        private static readonly MediaThumbnail[] EmptyThumbnails = new MediaThumbnail[0];

        public string FileName { get; private set; }
        public long Size { get; private set; }
        public string MimeType { get; private set; }
        public string LocalPath { get; private set; }
        public string LocalFullPath { get; private set; }
        public string Caption { get; private set; }
        public IList<MessageEntity> CaptionEntities { get; private set; }
        public TelegramMediaFile File { get; private set; }
        public IList<MediaThumbnail> Thumbnails { get; private set; }

        private static MessageEntity[] CopyEntities(IList<MessageEntity> source)
        {
            var arr = new MessageEntity[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static MediaThumbnail[] CopyThumbnails(IList<MediaThumbnail> source)
        {
            var arr = new MediaThumbnail[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public sealed class MessageContentVoice : MessageContent
    {
        private static readonly byte[] EmptyWaveform = new byte[0];

        public MessageContentVoice(TimeSpan duration, string localPath, byte[] waveform)
            : this(duration, localPath, waveform, null, 0, null, null, null)
        {
        }

        public MessageContentVoice(TimeSpan duration, string localPath, byte[] waveform,
            TelegramMediaFile file, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            Duration = duration;
            LocalPath = localPath ?? string.Empty;
            Waveform = CopyBytes(waveform);
            File = file;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public TimeSpan Duration { get; private set; }
        public string LocalPath { get; private set; }
        public byte[] Waveform { get; private set; }
        public TelegramMediaFile File { get; private set; }

        private static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return EmptyWaveform;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    public sealed class MessageContentSticker : MessageContent
    {
        public MessageContentSticker(string emoji, string localPath)
            : this(emoji, localPath, null, null, 0, null, null, null)
        {
        }

        public MessageContentSticker(string emoji, string localPath, TelegramMediaFile file,
            IList<MediaThumbnail> thumbnails, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            Emoji = emoji ?? string.Empty;
            LocalPath = localPath ?? string.Empty;
            File = file;
            Thumbnails = thumbnails != null ? CopyThumbnails(thumbnails) : EmptyThumbnails;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly MediaThumbnail[] EmptyThumbnails = new MediaThumbnail[0];

        public string Emoji { get; private set; }
        public string LocalPath { get; private set; }
        public TelegramMediaFile File { get; private set; }
        public IList<MediaThumbnail> Thumbnails { get; private set; }

        private static MediaThumbnail[] CopyThumbnails(IList<MediaThumbnail> source)
        {
            var arr = new MediaThumbnail[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public enum ServiceMessageKind
    {
        Unknown = 0,
        ChatCreated = 1,
        ChatTitleChanged = 2,
        ChatPhotoChanged = 3,
        UserJoined = 4,
        UserLeft = 5,
        UserKicked = 6,
        PinnedMessage = 7,
        ChannelCreated = 8,
        MigratedTo = 9,
        MigratedFrom = 10,
        PhoneCall = 11
    }

    public sealed class MessageContentService : MessageContent
    {
        public MessageContentService(ServiceMessageKind kind, string displayText)
            : this(kind, displayText, 0, null, null, null)
        {
        }

        public MessageContentService(ServiceMessageKind kind, string displayText,
            uint rawConstructorId, string unsupportedSummary,
            IList<MessageReaction> reactions, IList<MessageInlineButtonRow> buttonRows)
        {
            Kind = kind;
            DisplayText = displayText ?? string.Empty;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public ServiceMessageKind Kind { get; private set; }
        public string DisplayText { get; private set; }
    }

    /// <summary>
    /// Video file. Round "video note" messages set <see cref="IsVideoNote"/>;
    /// the UI typically renders them as a circular avatar-shaped bubble.
    /// Animations (GIFs / muted MP4 stickers) set <see cref="IsAnimation"/>.
    /// </summary>
    public sealed class MessageContentVideo : MessageContent
    {
        public MessageContentVideo(TimeSpan duration, int width, int height, long size,
            string localThumbPath, string localFullPath, string caption,
            bool isVideoNote, bool isAnimation)
            : this(duration, width, height, size, localThumbPath, localFullPath, caption,
                  isVideoNote, isAnimation, null, null, null, 0, null, null, null)
        {
        }

        public MessageContentVideo(TimeSpan duration, int width, int height, long size,
            string localThumbPath, string localFullPath, string caption,
            bool isVideoNote, bool isAnimation, IList<MessageEntity> captionEntities,
            TelegramMediaFile file, IList<MediaThumbnail> thumbnails,
            uint rawConstructorId = 0, string unsupportedSummary = null,
            IList<MessageReaction> reactions = null, IList<MessageInlineButtonRow> buttonRows = null)
        {
            Duration = duration;
            Width = width;
            Height = height;
            Size = size;
            LocalThumbPath = localThumbPath ?? string.Empty;
            LocalFullPath = localFullPath ?? string.Empty;
            Caption = caption ?? string.Empty;
            IsVideoNote = isVideoNote;
            IsAnimation = isAnimation;
            CaptionEntities = captionEntities != null ? CopyEntities(captionEntities) : EmptyEntities;
            File = file;
            Thumbnails = thumbnails != null ? CopyThumbnails(thumbnails) : EmptyThumbnails;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly MessageEntity[] EmptyEntities = new MessageEntity[0];
        private static readonly MediaThumbnail[] EmptyThumbnails = new MediaThumbnail[0];

        public TimeSpan Duration { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long Size { get; private set; }
        public string LocalThumbPath { get; private set; }
        public string LocalFullPath { get; private set; }
        public string Caption { get; private set; }
        public bool IsVideoNote { get; private set; }
        public bool IsAnimation { get; private set; }
        public IList<MessageEntity> CaptionEntities { get; private set; }
        public TelegramMediaFile File { get; private set; }
        public IList<MediaThumbnail> Thumbnails { get; private set; }

        private static MessageEntity[] CopyEntities(IList<MessageEntity> source)
        {
            var arr = new MessageEntity[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static MediaThumbnail[] CopyThumbnails(IList<MediaThumbnail> source)
        {
            var arr = new MediaThumbnail[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    /// <summary>Audio (music / podcast). Distinct from voice messages.</summary>
    public sealed class MessageContentAudio : MessageContent
    {
        public MessageContentAudio(TimeSpan duration, string title, string performer,
            long size, string localPath, string caption)
            : this(duration, title, performer, size, localPath, localPath, caption,
                  null, null, null, 0, null, null, null)
        {
        }

        public MessageContentAudio(TimeSpan duration, string title, string performer,
            long size, string localPath, string localFullPath, string caption,
            IList<MessageEntity> captionEntities, TelegramMediaFile file,
            IList<MediaThumbnail> thumbnails, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            Duration = duration;
            Title = title ?? string.Empty;
            Performer = performer ?? string.Empty;
            Size = size;
            LocalPath = localPath ?? string.Empty;
            LocalFullPath = localFullPath ?? string.Empty;
            Caption = caption ?? string.Empty;
            CaptionEntities = captionEntities != null ? CopyEntities(captionEntities) : EmptyEntities;
            File = file;
            Thumbnails = thumbnails != null ? CopyThumbnails(thumbnails) : EmptyThumbnails;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly MessageEntity[] EmptyEntities = new MessageEntity[0];
        private static readonly MediaThumbnail[] EmptyThumbnails = new MediaThumbnail[0];

        public TimeSpan Duration { get; private set; }
        public string Title { get; private set; }
        public string Performer { get; private set; }
        public long Size { get; private set; }
        public string LocalPath { get; private set; }
        public string LocalFullPath { get; private set; }
        public string Caption { get; private set; }
        public IList<MessageEntity> CaptionEntities { get; private set; }
        public TelegramMediaFile File { get; private set; }
        public IList<MediaThumbnail> Thumbnails { get; private set; }

        private static MessageEntity[] CopyEntities(IList<MessageEntity> source)
        {
            var arr = new MessageEntity[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static MediaThumbnail[] CopyThumbnails(IList<MediaThumbnail> source)
        {
            var arr = new MediaThumbnail[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }

    public sealed class MessageContentContact : MessageContent
    {
        public MessageContentContact(string firstName, string lastName, string phoneNumber, long? userId)
            : this(firstName, lastName, phoneNumber, userId, 0, null, null, null)
        {
        }

        public MessageContentContact(string firstName, string lastName, string phoneNumber,
            long? userId, uint rawConstructorId, string unsupportedSummary,
            IList<MessageReaction> reactions, IList<MessageInlineButtonRow> buttonRows)
        {
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            PhoneNumber = phoneNumber ?? string.Empty;
            UserId = userId;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string PhoneNumber { get; private set; }
        public long? UserId { get; private set; }

        public string DisplayName
        {
            get
            {
                string both = (FirstName + " " + LastName).Trim();
                return string.IsNullOrEmpty(both) ? PhoneNumber : both;
            }
        }
    }

    /// <summary>
    /// Geographic location. <see cref="VenueTitle"/> / <see cref="VenueAddress"/>
    /// are non-empty when the message originates from <c>messageMediaVenue</c>.
    /// </summary>
    public sealed class MessageContentLocation : MessageContent
    {
        public MessageContentLocation(double latitude, double longitude,
            string venueTitle, string venueAddress)
            : this(latitude, longitude, venueTitle, venueAddress, 0, null, null, null)
        {
        }

        public MessageContentLocation(double latitude, double longitude,
            string venueTitle, string venueAddress, uint rawConstructorId,
            string unsupportedSummary, IList<MessageReaction> reactions,
            IList<MessageInlineButtonRow> buttonRows)
        {
            Latitude = latitude;
            Longitude = longitude;
            VenueTitle = venueTitle ?? string.Empty;
            VenueAddress = venueAddress ?? string.Empty;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public string VenueTitle { get; private set; }
        public string VenueAddress { get; private set; }
        public bool IsVenue { get { return !string.IsNullOrEmpty(VenueTitle); } }
    }

    public sealed class MessageContentPoll : MessageContent
    {
        public MessageContentPoll(string question, IList<string> answers, int totalVoters, bool isClosed)
            : this(question, answers, null, totalVoters, isClosed, false, false, 0L, 0, null, null, null)
        {
        }

        public MessageContentPoll(string question, IList<PollOption> options,
            int totalVoters, bool isClosed, bool multipleAnswers, bool isQuiz,
            long pollId = 0, uint rawConstructorId = 0, string unsupportedSummary = null,
            IList<MessageReaction> reactions = null, IList<MessageInlineButtonRow> buttonRows = null)
            : this(question, (IList<string>)null, options, totalVoters, isClosed, multipleAnswers,
                  isQuiz, pollId, rawConstructorId, unsupportedSummary, reactions, buttonRows)
        {
        }

        private MessageContentPoll(string question, IList<string> answers,
            IList<PollOption> options, int totalVoters, bool isClosed,
            bool multipleAnswers, bool isQuiz, long pollId, uint rawConstructorId,
            string unsupportedSummary, IList<MessageReaction> reactions,
            IList<MessageInlineButtonRow> buttonRows)
        {
            Question = question ?? string.Empty;
            Options = options != null ? CopyOptions(options) : BuildOptionsFromAnswers(answers);
            Answers = BuildAnswersFromOptions(Options);
            TotalVoters = totalVoters;
            IsClosed = isClosed;
            MultipleAnswers = multipleAnswers;
            IsQuiz = isQuiz;
            PollId = pollId;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        private static readonly PollOption[] EmptyOptions = new PollOption[0];

        public string Question { get; private set; }
        public IList<string> Answers { get; private set; }
        public IList<PollOption> Options { get; private set; }
        public int TotalVoters { get; private set; }
        public bool IsClosed { get; private set; }
        public bool MultipleAnswers { get; private set; }
        public bool IsQuiz { get; private set; }
        public long PollId { get; private set; }

        private static string[] CopyToReadOnly(IList<string> source)
        {
            var arr = new string[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i] ?? string.Empty;
            return arr;
        }

        private static PollOption[] CopyOptions(IList<PollOption> source)
        {
            var arr = new PollOption[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static PollOption[] BuildOptionsFromAnswers(IList<string> source)
        {
            if (source == null || source.Count == 0) return EmptyOptions;

            var arr = new PollOption[source.Count];
            for (int i = 0; i < source.Count; i++)
                arr[i] = new PollOption(source[i], null, 0, false, false);
            return arr;
        }

        private static string[] BuildAnswersFromOptions(IList<PollOption> source)
        {
            if (source == null || source.Count == 0) return new string[0];

            var arr = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
                arr[i] = source[i] != null ? source[i].Text : string.Empty;
            return arr;
        }
    }

    public sealed class MessageContentWebPage : MessageContent
    {
        public MessageContentWebPage(string body, string url, string siteName,
            string title, string description, string thumbPath)
            : this(body, url, siteName, title, description, thumbPath, null, null, 0, null, null, null)
        {
        }

        public MessageContentWebPage(string body, string url, string siteName,
            string title, string description, string thumbPath,
            string displayUrl, MediaThumbnail thumb, uint rawConstructorId = 0,
            string unsupportedSummary = null, IList<MessageReaction> reactions = null,
            IList<MessageInlineButtonRow> buttonRows = null)
        {
            Body = body ?? string.Empty;
            Url = url ?? string.Empty;
            SiteName = siteName ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            ThumbPath = thumbPath ?? string.Empty;
            DisplayUrl = displayUrl ?? string.Empty;
            Thumb = thumb;
            ThumbUrl = thumb != null ? thumb.Url : string.Empty;
            SetTelegramMetadata(rawConstructorId, unsupportedSummary, reactions, buttonRows);
        }

        public string Body { get; private set; }
        public string Url { get; private set; }
        public string DisplayUrl { get; private set; }
        public string SiteName { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public string ThumbPath { get; private set; }
        public string ThumbUrl { get; private set; }
        public MediaThumbnail Thumb { get; private set; }
    }

    /// <summary>
    /// Catch-all for media types we do not yet model. Renders as "[unsupported]"
    /// fallback in UI. Carries a hint string identifying the wire ctor name
    /// (e.g. "messageMediaInvoice") so the bubble can show a placeholder
    /// rather than a generic blob.
    /// </summary>
    public sealed class MessageContentUnsupported : MessageContent
    {
        public MessageContentUnsupported() : this(string.Empty) { }
        public MessageContentUnsupported(string mediaKindHint)
            : this(mediaKindHint, 0, null, null, null)
        {
        }

        public MessageContentUnsupported(string mediaKindHint, uint rawConstructorId,
            string summary, IList<MessageReaction> reactions,
            IList<MessageInlineButtonRow> buttonRows)
        {
            MediaKindHint = mediaKindHint ?? string.Empty;
            SetTelegramMetadata(rawConstructorId, summary, reactions, buttonRows);
        }

        public string MediaKindHint { get; private set; }
    }
}
