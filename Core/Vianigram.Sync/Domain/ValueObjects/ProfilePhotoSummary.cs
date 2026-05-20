// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Tiny stub of a userProfilePhoto / chatPhoto. Carries the photo_id and
    /// stripped-thumb byte length; full media descriptor lives in Vianigram.Media.
    ///
    /// HasPhoto = false signals userProfilePhotoEmpty / chatPhotoEmpty.
    /// </summary>
    public sealed class ProfilePhotoSummary
    {
        public static readonly ProfilePhotoSummary Empty = new ProfilePhotoSummary(false, 0, 0);

        public ProfilePhotoSummary(bool hasPhoto, long photoId, int dcId)
        {
            HasPhoto = hasPhoto;
            PhotoId = photoId;
            DcId = dcId;
        }

        public bool HasPhoto { get; private set; }
        public long PhotoId { get; private set; }
        public int DcId { get; private set; }
    }
}
