// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomTagParseTests
    {
        #region TryParseByKeywordOrTag

        [Fact]
        public void TryParseByKeywordOrTag_ValidKeyword_ReturnsTrue()
        {
            var result = DicomTag.TryParseByKeywordOrTag("PatientID", out var tag);

            Assert.True(result);
            Assert.Equal(DicomTag.PatientID, tag);
        }

        [Fact]
        public void TryParseByKeywordOrTag_ValidKeyword_ReturnsCorrectTag()
        {
            DicomTag.TryParseByKeywordOrTag("StudyInstanceUID", out var tag);

            Assert.Equal(DicomTag.StudyInstanceUID, tag);
        }

        [Theory]
        [InlineData("00100020")] // PatientID in hex (no dashes/parens)
        [InlineData("0020000D")] // StudyInstanceUID in hex
        [InlineData("00080060")] // Modality in hex
        public void TryParseByKeywordOrTag_ValidHexTag_ReturnsTrue(string hexTag)
        {
            var result = DicomTag.TryParseByKeywordOrTag(hexTag, out _);

            Assert.True(result);
        }

        [Fact]
        public void TryParseByKeywordOrTag_ValidHexTag_ReturnsCorrectTag()
        {
            DicomTag.TryParseByKeywordOrTag("00100020", out var tag);

            Assert.Equal(DicomTag.PatientID, tag);
        }

        [Fact]
        public void TryParseByKeywordOrTag_UnknownKeyword_ReturnsFalse()
        {
            var result = DicomTag.TryParseByKeywordOrTag("NotARealDicomKeyword", out var tag);

            Assert.False(result);
            Assert.Null(tag);
        }

        [Fact]
        public void TryParseByKeywordOrTag_EmptyString_ReturnsFalse()
        {
            var result = DicomTag.TryParseByKeywordOrTag(string.Empty, out var tag);

            Assert.False(result);
            Assert.Null(tag);
        }

        [Theory]
        [InlineData("PatientName")]
        [InlineData("StudyDate")]
        [InlineData("Modality")]
        [InlineData("SeriesInstanceUID")]
        [InlineData("SOPInstanceUID")]
        public void TryParseByKeywordOrTag_WellKnownKeywords_Roundtrip(string keyword)
        {
            var result = DicomTag.TryParseByKeywordOrTag(keyword, out var tag);

            Assert.True(result);
            Assert.Equal(keyword, tag.DictionaryEntry.Keyword);
        }

        #endregion
    }
}
