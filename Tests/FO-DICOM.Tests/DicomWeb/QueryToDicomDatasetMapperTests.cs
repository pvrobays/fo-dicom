// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    /// <summary>
    /// Direct unit tests for <see cref="QueryToDicomDatasetMapper.Map"/>.
    /// These tests bypass the HTTP layer and exercise the mapper in isolation using
    /// a <see cref="QueryCollection"/> built directly from a dictionary.
    /// </summary>
    [Collection(TestCollections.General)]
    public class QueryToDicomDatasetMapperTests
    {
        private static QueryCollection Q(Dictionary<string, StringValues> d) => new QueryCollection(d);
        private static QueryCollection Q() => new QueryCollection();

        #region Reserved parameters

        [FactForNetCore]
        public void Map_NoParams_CreatesRequestWithDefaultOptions()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study, Q());

            Assert.Equal(DicomQueryRetrieveLevel.Study, request.Level);
            Assert.False(request.Options.IsFuzzyMatching);
            Assert.Equal(0, request.Options.Limit);
            Assert.Equal(0, request.Options.Offset);
        }

        [FactForNetCore]
        public void Map_FuzzyMatchingTrue_SetOnOptions()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["fuzzymatching"] = "true" }));

            Assert.True(request.Options.IsFuzzyMatching);
        }

        [FactForNetCore]
        public void Map_FuzzyMatchingFalse_SetOnOptions()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["fuzzymatching"] = "false" }));

            Assert.False(request.Options.IsFuzzyMatching);
        }

        [FactForNetCore]
        public void Map_Limit_SetOnOptions()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["limit"] = "25" }));

            Assert.Equal(25, request.Options.Limit);
        }

        [FactForNetCore]
        public void Map_Offset_SetOnOptions()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["offset"] = "10" }));

            Assert.Equal(10, request.Options.Offset);
        }

        [FactForNetCore]
        public void Map_InvalidLimit_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["limit"] = "notanumber" })));
        }

        [FactForNetCore]
        public void Map_InvalidOffset_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["offset"] = "notanumber" })));
        }

        [FactForNetCore]
        public void Map_NegativeOffset_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["offset"] = "-1" })));
        }

        [FactForNetCore]
        public void Map_ReservedParams_NotAddedToDataset()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues>
                {
                    ["fuzzymatching"] = "true",
                    ["limit"] = "5",
                    ["offset"] = "2"
                }));

            // Options are set...
            Assert.True(request.Options.IsFuzzyMatching);
            Assert.Equal(5, request.Options.Limit);
            Assert.Equal(2, request.Options.Offset);

            // ...but none of the reserved param names correspond to DICOM tags and must not be
            // misinterpreted as attribute match keys.
            // The dataset only has minimum required response tags, not random extra attributes.
            // Verify by checking a tag that would only appear if params were mis-mapped as match keys.
            // "limit" and "offset" have no DICOM keyword equivalent, so no spurious tag should appear.
            // The simplest guard: query param count == 3, dataset should only have the standard
            // minimum required tags (i.e., not some unexpected extra attribute from the param names).
            // PatientID IS a minimum required tag, so we verify its value is empty (not "5" or "2").
            Assert.Equal(string.Empty, request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        #endregion

        #region Match attribute mapping — keyword and hex tag

        [FactForNetCore]
        public void Map_MatchByKeyword_AddedToDataset()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["PatientID"] = "11235813" }));

            Assert.Equal("11235813", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public void Map_MatchByHexTag_AddedToDataset()
        {
            // PatientID = (0010,0020) -> "00100020"
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["00100020"] = "11235813" }));

            Assert.Equal("11235813", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public void Map_MultipleMatchParams_AllAddedToDataset()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues>
                {
                    ["PatientID"] = "11235813",
                    ["StudyDate"] = "20130509"
                }));

            Assert.Equal("11235813", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            Assert.Equal("20130509", request.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty));
        }

        [FactForNetCore]
        public void Map_UnknownKeyword_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["NotADicomKeyword"] = "value" })));
        }

        #endregion

        #region includefield parameter

        [FactForNetCore]
        public void Map_IncludeFieldByKeyword_AddedEmptyToDataset()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "ReferringPhysicianName" }));

            // The tag must be present; the value is an empty placeholder (not a match filter)
            Assert.True(request.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public void Map_IncludeFieldByHexTag_AddedToDataset()
        {
            // ReferringPhysicianName = (0008,0090)
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "00080090" }));

            Assert.True(request.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public void Map_IncludeFieldCsv_AllAddedToDataset()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "00080090,00100010" }));

            Assert.True(request.Dataset.Contains(DicomTag.ReferringPhysicianName));
            Assert.True(request.Dataset.Contains(DicomTag.PatientName));
        }

        [FactForNetCore]
        public void Map_IncludeFieldAll_SetsIncludeAllFields()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "all" }));

            Assert.True(request.IncludeAllFields);
        }

        [FactForNetCore]
        public void Map_IncludeFieldAll_CaseInsensitive()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "ALL" }));

            Assert.True(request.IncludeAllFields);
        }

        [FactForNetCore]
        public void Map_IncludeFieldAllWithOther_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "all,PatientName" })));
        }

        [FactForNetCore]
        public void Map_IncludeFieldUnknownTag_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "NotATag" })));
        }

        [FactForNetCore]
        public void Map_IncludeFieldBareSqTag_AddsEmptySequence()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "RequestAttributesSequence" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.Empty(seq.Items);
        }

        #endregion

        #region includefield — dot notation

        [FactForNetCore]
        public void Map_IncludeFieldDotNotationHexTags_CreatesNestedSequence()
        {
            // includefield=00081115.00080060  (ReferencedSeriesSequence.Modality)
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "00081115.00080060" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.ReferencedSeriesSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.True(seq.Items[0].Contains(DicomTag.Modality));
        }

        [FactForNetCore]
        public void Map_IncludeFieldDotNotationKeywords_CreatesNestedSequence()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["includefield"] = "OtherPatientIDsSequence.PatientID" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.True(seq.Items[0].Contains(DicomTag.PatientID));
        }

        [FactForNetCore]
        public void Map_IncludeFieldDotNotation_InvalidSegment_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "NotATag.PatientID" })));
        }

        [FactForNetCore]
        public void Map_IncludeFieldDotNotation_NonSqIntermediate_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "PatientID.PatientName" })));
        }

        #endregion

        #region Match attribute — dot notation

        [FactForNetCore]
        public void Map_MatchDotNotationHexTags_CreatesNestedFilter()
        {
            // ?00101002.00100020=11235813  (OtherPatientIDsSequence.PatientID)
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["00101002.00100020"] = "11235813" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public void Map_MatchDotNotationKeywords_CreatesNestedFilter()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["OtherPatientIDsSequence.PatientID"] = "11235813" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public void Map_MatchDotNotation_InvalidSegment_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["NotATag.PatientID"] = "value" })));
        }

        [FactForNetCore]
        public void Map_MatchDotNotation_NonSqIntermediate_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["PatientID.PatientName"] = "value" })));
        }

        [FactForNetCore]
        public void Map_TwoDotNotationParamsSameSequence_MergedIntoSameItem()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues>
                {
                    ["OtherPatientIDsSequence.PatientID"] = "11235813",
                    ["OtherPatientIDsSequence.PatientName"] = "SMITH"
                }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            Assert.Equal("SMITH", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        [FactForNetCore]
        public void Map_BareSqTagAsMatchParam_TreatedAsIncludeField()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["RequestAttributesSequence"] = "ignored" }));

            Assert.True(request.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.Empty(seq.Items);
        }

        #endregion

        #region Date range matching (PS3.4 C.2.2.2.5)

        [FactForNetCore]
        public void Map_StudyDate_BoundedRange_StoredAsDateRange()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyDate"] = "20230101-20231231" }));

            var range = request.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(new DateTime(2023, 1, 1), range.Minimum);
            Assert.Equal(new DateTime(2023, 12, 31), range.Maximum);
        }

        [FactForNetCore]
        public void Map_StudyDate_OpenStartRange_MinIsMinValue()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyDate"] = "-20231231" }));

            var range = request.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(DateTime.MinValue, range.Minimum);
            Assert.Equal(new DateTime(2023, 12, 31), range.Maximum);
        }

        [FactForNetCore]
        public void Map_StudyDate_OpenEndRange_MaxIsMaxValue()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyDate"] = "20230101-" }));

            var range = request.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(new DateTime(2023, 1, 1), range.Minimum);
            Assert.Equal(DateTime.MaxValue, range.Maximum);
        }

        [FactForNetCore]
        public void Map_StudyDate_SingleDate_StoredAsRawString()
        {
            // A single date value (no hyphen) must NOT be parsed as a range
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyDate"] = "20230101" }));

            Assert.Equal("20230101", request.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty));
        }

        [FactForNetCore]
        public void Map_NonDateTagWithHyphen_StoredAsRawString()
        {
            // PatientName is LO, not a date VR — a hyphen in the value must not trigger range parsing
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["PatientName"] = "SMITH-JONES" }));

            Assert.Equal("SMITH-JONES", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        #endregion

        #region UID list matching (PS3.18 Section 8.3.4.1)

        [FactForNetCore]
        public void Map_StudyInstanceUID_SingleUid_StoredAsSingleValue()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyInstanceUID"] = "1.2.3.4" }));

            var uids = request.Dataset.GetValues<string>(DicomTag.StudyInstanceUID);
            Assert.Single(uids);
            Assert.Equal("1.2.3.4", uids[0]);
        }

        [FactForNetCore]
        public void Map_StudyInstanceUID_TwoUids_StoredAsTwoValues()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["StudyInstanceUID"] = "1.2.3,4.5.6" }));

            var uids = request.Dataset.GetValues<string>(DicomTag.StudyInstanceUID);
            Assert.Equal(2, uids.Length);
            Assert.Equal("1.2.3", uids[0]);
            Assert.Equal("4.5.6", uids[1]);
        }

        [FactForNetCore]
        public void Map_NonUidTagWithComma_StoredAsRawString()
        {
            // PatientName is PN, not UI — comma must not trigger UID list splitting
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues> { ["PatientName"] = "SMITH,JONES" }));

            Assert.Equal("SMITH,JONES", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        #endregion

        #region strictParsing parameter

        [FactForNetCore]
        public void Map_UnknownQueryParam_StrictMode_Throws()
        {
            // Default (strictParsing = true): unknown param throws
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["NotADicomKeyword"] = "value" }),
                    strictParsing: true));
        }

        [FactForNetCore]
        public void Map_UnknownQueryParam_LenientMode_IsSkipped()
        {
            // Lenient (strictParsing = false): unknown param is silently skipped; valid ones still work
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues>
                {
                    ["NotADicomKeyword"] = "shouldBeIgnored",
                    ["PatientID"] = "12345"
                }),
                strictParsing: false);

            Assert.Equal("12345", request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public void Map_UnknownIncludeField_StrictMode_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "NotADicomTag" }),
                    strictParsing: true));
        }

        [FactForNetCore]
        public void Map_UnknownIncludeField_LenientMode_IsSkipped()
        {
            // Unknown includefield is skipped; a valid one in the same CSV is still added
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                Q(new Dictionary<string, StringValues>
                {
                    ["includefield"] = "NotADicomTag,ReferringPhysicianName"
                }),
                strictParsing: false);

            // The invalid value was skipped, but the valid one was applied
            Assert.True(request.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public void Map_InvalidLimitOrOffset_AlwaysThrows_RegardlessOfStrictMode()
        {
            // Structural errors (bad limit/offset) always throw even in lenient mode
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["limit"] = "notanumber" }),
                    strictParsing: false));
        }

        [FactForNetCore]
        public void Map_InvalidIncludeFieldAllCombination_AlwaysThrows_RegardlessOfStrictMode()
        {
            // includefield=all combined with other values is a structural error, always throws
            Assert.Throws<InvalidOperationException>(() =>
                QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Study,
                    Q(new Dictionary<string, StringValues> { ["includefield"] = "all,PatientName" }),
                    strictParsing: false));
        }

        #endregion

        #region Level propagation

        [FactForNetCore]
        public void Map_SeriesLevel_SetsCorrectLevel()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Series, Q());
            Assert.Equal(DicomQueryRetrieveLevel.Series, request.Level);
        }

        [FactForNetCore]
        public void Map_ImageLevel_SetsCorrectLevel()
        {
            var request = QueryToDicomDatasetMapper.Map(DicomQueryRetrieveLevel.Image, Q());
            Assert.Equal(DicomQueryRetrieveLevel.Image, request.Level);
        }

        #endregion
    }
}

#endif
