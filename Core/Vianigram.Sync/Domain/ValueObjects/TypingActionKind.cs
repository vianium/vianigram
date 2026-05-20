// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Coarse-grained typing/composing action. Maps the ~13 sendMessageActionXxx
    /// TL constructors into a small, stable enum. Unknown actions collapse to
    /// <see cref="Cancel"/> (no-op) so we don't leak TL surface area outward.
    /// </summary>
    public enum TypingActionKind
    {
        Cancel = 0,
        Typing = 1,
        RecordVideo = 2,
        UploadVideo = 3,
        RecordAudio = 4,
        UploadAudio = 5,
        UploadPhoto = 6,
        UploadDocument = 7,
        GeoLocation = 8,
        ChooseContact = 9,
        GamePlay = 10,
        RecordRound = 11,
        UploadRound = 12,
        SpeakingInGroupCall = 13,
        // Modern actions (layer 214+).
        ChooseSticker = 14,        // sendMessageChooseStickerAction#b05ac6b1
        EmojiInteraction = 15,     // sendMessageEmojiInteraction#25972bcb
        EmojiInteractionSeen = 16, // sendMessageEmojiInteractionSeen#b665d5dc
        ImportingHistory = 17,     // sendMessageHistoryImportAction#dbda9246
        Other = 99
    }
}
