// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using System;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomQidoRequestTests
    {
        #region Constructors and defaults

        [Fact]
        public void Constructor_StudyLevel_SetsLevelCorrectly()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Study);

            Assert.Equal(DicomQueryRetrieveLevel.Study, request.Level);
        }

        [Fact]
        public void Constructor_SeriesLevel_SetsLevelCorrectly()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Series);

            Assert.Equal(DicomQueryRetrieveLevel.Series, request.Level);
        }

        [Fact]
        public void Constructor_ImageLevel_SetsLevelCorrectly()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Image);

            Assert.Equal(DicomQueryRetrieveLevel.Image, request.Level);
        }

        [Fact]
        public void Constructor_NullOptions_UsesDefaults()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Study);

            Assert.NotNull(request.Options);
            Assert.Equal(0, request.Options.Limit);
            Assert.Equal(0, request.Options.Offset);
            Assert.False(request.Options.IsFuzzyMatching);
        }

        [Fact]
        public void Constructor_WithOptions_SetsOptionsCorrectly()
        {
            var options = new DicomQidoRequestOptions(true, 25, 10);
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Study, options);

            Assert.Equal(25, request.Options.Limit);
            Assert.Equal(10, request.Options.Offset);
            Assert.True(request.Options.IsFuzzyMatching);
        }

        [Fact]
        public void Constructor_DatasetNotNull()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Study);

            Assert.NotNull(request.Dataset);
        }

        #endregion

        #region Minimum required response tags (per DICOM PS3.18 sect_10.6.1.2.3)

        [Theory]
        [InlineData(0x0008, 0x0020)] // StudyDate
        [InlineData(0x0008, 0x0030)] // StudyTime
        [InlineData(0x0008, 0x0050)] // AccessionNumber
        [InlineData(0x0008, 0x0061)] // ModalitiesInStudy
        [InlineData(0x0008, 0x0090)] // ReferringPhysicianName
        [InlineData(0x0010, 0x0010)] // PatientName
        [InlineData(0x0010, 0x0020)] // PatientID
        [InlineData(0x0020, 0x000D)] // StudyInstanceUID
        [InlineData(0x0020, 0x0010)] // StudyID
        public void StudyLevel_ContainsMinimumRequiredResponseTag(int group, int element)
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Study);

            Assert.True(request.Dataset.Contains(new DicomTag((ushort)group, (ushort)element)),
                $"Study-level QIDO request is missing required response tag ({group:X4},{element:X4})");
        }

        [Theory]
        [InlineData(0x0008, 0x0060)] // Modality
        [InlineData(0x0020, 0x000E)] // SeriesInstanceUID
        [InlineData(0x0020, 0x0011)] // SeriesNumber
        [InlineData(0x0020, 0x1209)] // NumberOfSeriesRelatedInstances
        [InlineData(0x0040, 0x0244)] // PerformedProcedureStepStartDate
        [InlineData(0x0040, 0x0245)] // PerformedProcedureStepStartTime
        [InlineData(0x0040, 0x0275)] // RequestAttributesSequence
        public void SeriesLevel_ContainsMinimumRequiredResponseTag(int group, int element)
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Series);

            Assert.True(request.Dataset.Contains(new DicomTag((ushort)group, (ushort)element)),
                $"Series-level QIDO request is missing required response tag ({group:X4},{element:X4})");
        }

        [Fact]
        public void SeriesLevel_RequestAttributesSequence_ContainsRequiredChildTags()
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Series);

            Assert.True(request.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.NotEmpty(seq.Items);
            var item = seq.Items[0];
            Assert.True(item.Contains(DicomTag.ScheduledProcedureStepID),
                "RequestAttributesSequence item is missing ScheduledProcedureStepID");
            Assert.True(item.Contains(DicomTag.RequestedProcedureID),
                "RequestAttributesSequence item is missing RequestedProcedureID");
        }

        [Theory]
        [InlineData(0x0008, 0x0016)] // SOPClassUID
        [InlineData(0x0008, 0x0018)] // SOPInstanceUID
        [InlineData(0x0020, 0x0013)] // InstanceNumber
        [InlineData(0x0028, 0x0010)] // Rows
        [InlineData(0x0028, 0x0011)] // Columns
        [InlineData(0x0028, 0x0100)] // BitsAllocated
        [InlineData(0x0028, 0x0008)] // NumberOfFrames
        public void ImageLevel_ContainsMinimumRequiredResponseTag(int group, int element)
        {
            var request = new DicomQidoRequest(DicomQueryRetrieveLevel.Image);

            Assert.True(request.Dataset.Contains(new DicomTag((ushort)group, (ushort)element)),
                $"Image-level QIDO request is missing required response tag ({group:X4},{element:X4})");
        }

        #endregion

        #region CreateStudyQuery factory method

        [Fact]
        public void CreateStudyQuery_NoArguments_IsStudyLevel()
        {
            var request = DicomQidoRequest.CreateStudyQuery();

            Assert.Equal(DicomQueryRetrieveLevel.Study, request.Level);
        }

        [Fact]
        public void CreateStudyQuery_WithPatientId_SetsPatientIdInDataset()
        {
            var request = DicomQidoRequest.CreateStudyQuery(patientId: "12345");

            var value = request.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty);
            Assert.Equal("12345", value);
        }

        [Fact]
        public void CreateStudyQuery_WithPatientName_SetsPatientNameInDataset()
        {
            var request = DicomQidoRequest.CreateStudyQuery(patientName: "SMITH^JOHN");

            var value = request.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty);
            Assert.Equal("SMITH^JOHN", value);
        }

        [Fact]
        public void CreateStudyQuery_WithStudyInstanceUid_SetsStudyInstanceUidInDataset()
        {
            var uid = DicomUID.Generate().UID;
            var request = DicomQidoRequest.CreateStudyQuery(studyInstanceUid: uid);

            var value = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);
            Assert.Equal(uid, value);
        }

        [Fact]
        public void CreateStudyQuery_WithModalitiesInStudy_SetsModalitiesInDataset()
        {
            var request = DicomQidoRequest.CreateStudyQuery(modalitiesInStudy: "CT");

            var value = request.Dataset.GetSingleValueOrDefault(DicomTag.ModalitiesInStudy, string.Empty);
            Assert.Equal("CT", value);
        }

        [Fact]
        public void CreateStudyQuery_WithAccession_SetsAccessionInDataset()
        {
            var request = DicomQidoRequest.CreateStudyQuery(accession: "ACC123");

            var value = request.Dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty);
            Assert.Equal("ACC123", value);
        }

        [Fact]
        public void CreateStudyQuery_WithStudyDateRange_StudyDateWireValueIsRangeString()
        {
            var range = new DicomDateRange(new DateTime(2013, 1, 1), new DateTime(2013, 12, 31));
            var request = DicomQidoRequest.CreateStudyQuery(studyDateTime: range);

            // The dataset stores the DicomDateRange as a wire-format DA string "20130101-20131231"
            var wireValue = request.Dataset.GetSingleValue<string>(DicomTag.StudyDate);
            Assert.Equal("20130101-20131231", wireValue);
        }

        [Fact]
        public void CreateStudyQuery_WithOpenEndStudyDateRange_StudyDateWireValueIsOpenEndString()
        {
            // Open-end range: all dates from 2013-01-01 onwards
            var range = new DicomDateRange(new DateTime(2013, 1, 1), DateTime.MaxValue);
            var request = DicomQidoRequest.CreateStudyQuery(studyDateTime: range);

            var wireValue = request.Dataset.GetSingleValue<string>(DicomTag.StudyDate);
            Assert.Equal("20130101-", wireValue);
        }

        [Fact]
        public void CreateStudyQuery_WithOpenStartStudyDateRange_StudyDateWireValueIsOpenStartString()
        {
            // Open-start range: all dates up to and including 2013-12-31
            var range = new DicomDateRange(DateTime.MinValue, new DateTime(2013, 12, 31));
            var request = DicomQidoRequest.CreateStudyQuery(studyDateTime: range);

            var wireValue = request.Dataset.GetSingleValue<string>(DicomTag.StudyDate);
            Assert.Equal("-20131231", wireValue);
        }

        [Fact]
        public void CreateStudyQuery_NullStudyDateTime_StudyDateIsEmptyString()
        {
            var request = DicomQidoRequest.CreateStudyQuery(studyDateTime: null);

            var wireValue = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty);
            Assert.Equal(string.Empty, wireValue);
        }

        [Fact]
        public void CreateStudyQuery_WithStudyDateRange_RangeCanBeReadBackFromDataset()
        {
            var min = new DateTime(2013, 1, 1);
            var max = new DateTime(2013, 12, 31);
            var range = new DicomDateRange(min, max);
            var request = DicomQidoRequest.CreateStudyQuery(studyDateTime: range);

            var readBack = request.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(min, readBack.Minimum);
            Assert.Equal(max, readBack.Maximum);
        }

        #endregion

        #region CreateSeriesQuery factory method

        [Fact]
        public void CreateSeriesQuery_NoArguments_IsSeriesLevel()
        {
            var request = DicomQidoRequest.CreateSeriesQuery();

            Assert.Equal(DicomQueryRetrieveLevel.Series, request.Level);
        }

        [Fact]
        public void CreateSeriesQuery_WithStudyInstanceUid_SetsStudyInstanceUidInDataset()
        {
            var uid = DicomUID.Generate().UID;
            var request = DicomQidoRequest.CreateSeriesQuery(studyInstanceUid: uid);

            Assert.Equal(uid,
                request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [Fact]
        public void CreateSeriesQuery_NullStudyInstanceUid_StudyInstanceUidIsEmpty()
        {
            // null studyInstanceUid = relational "all series" query — no parent scope
            var request = DicomQidoRequest.CreateSeriesQuery(studyInstanceUid: null);

            Assert.Equal(string.Empty,
                request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [Fact]
        public void CreateSeriesQuery_WithModality_SetsModalityInDataset()
        {
            var request = DicomQidoRequest.CreateSeriesQuery(modality: "CT");

            Assert.Equal("CT",
                request.Dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty));
        }

        [Fact]
        public void CreateSeriesQuery_WithSeriesInstanceUid_SetsSeriesInstanceUidInDataset()
        {
            var uid = DicomUID.Generate().UID;
            var request = DicomQidoRequest.CreateSeriesQuery(seriesInstanceUid: uid);

            Assert.Equal(uid,
                request.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        #endregion

        #region CreateInstanceQuery factory method

        [Fact]
        public void CreateInstanceQuery_NoArguments_IsImageLevel()
        {
            var request = DicomQidoRequest.CreateInstanceQuery();

            Assert.Equal(DicomQueryRetrieveLevel.Image, request.Level);
        }

        [Fact]
        public void CreateInstanceQuery_WithStudyAndSeriesUid_SetsBothInDataset()
        {
            var studyUid = DicomUID.Generate().UID;
            var seriesUid = DicomUID.Generate().UID;
            var request = DicomQidoRequest.CreateInstanceQuery(
                studyInstanceUid: studyUid,
                seriesInstanceUid: seriesUid);

            Assert.Equal(studyUid,
                request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal(seriesUid,
                request.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        [Fact]
        public void CreateInstanceQuery_WithSopInstanceUid_SetsSopInstanceUidInDataset()
        {
            var uid = DicomUID.Generate().UID;
            var request = DicomQidoRequest.CreateInstanceQuery(sopInstanceUid: uid);

            Assert.Equal(uid,
                request.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty));
        }

        [Fact]
        public void CreateInstanceQuery_NullStudyAndSeriesUid_AllScopeUidsAreEmpty()
        {
            // null uids = relational "all instances" query
            var request = DicomQidoRequest.CreateInstanceQuery(
                studyInstanceUid: null, seriesInstanceUid: null);

            Assert.Equal(string.Empty,
                request.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal(string.Empty,
                request.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        #endregion
    }
}
