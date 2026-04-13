// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.IO.Buffer;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomBulkDataHelperTests
    {
        private const string BaseUrl = "/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9";

        // ── IsBulkDataVR ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("OB")] [InlineData("OD")] [InlineData("OF")]
        [InlineData("OL")] [InlineData("OV")] [InlineData("OW")] [InlineData("UN")]
        public void IsBulkDataVR_BulkVR_ReturnsTrue(string vrCode)
        {
            var vr = DicomVR.Parse(vrCode);
            Assert.True(DicomBulkDataHelper.IsBulkDataVR(vr));
        }

        [Theory]
        [InlineData("LO")] [InlineData("CS")] [InlineData("UI")] [InlineData("SQ")]
        [InlineData("DA")] [InlineData("TM")] [InlineData("PN")] [InlineData("DS")]
        public void IsBulkDataVR_NonBulkVR_ReturnsFalse(string vrCode)
        {
            var vr = DicomVR.Parse(vrCode);
            Assert.False(DicomBulkDataHelper.IsBulkDataVR(vr));
        }

        // ── FormatTagForUrl ───────────────────────────────────────────────────

        [Fact]
        public void FormatTagForUrl_PixelData_Returns7FE00010()
        {
            Assert.Equal("7FE00010", DicomBulkDataHelper.FormatTagForUrl(DicomTag.PixelData));
        }

        [Fact]
        public void FormatTagForUrl_PatientName_Returns00100010()
        {
            Assert.Equal("00100010", DicomBulkDataHelper.FormatTagForUrl(DicomTag.PatientName));
        }

        // ── ReplaceBulkDataWithUris — top-level replacement ───────────────────

        [Fact]
        public void Replace_TopLevelOW_ReplacedWithBulkDataUri()
        {
            var pixels = new byte[] { 1, 2, 3, 4 };
            var ds = new DicomDataset
            {
                new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixels))
            };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.PixelData);
            Assert.NotNull(elem);
            var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
            Assert.Equal($"{BaseUrl}/bulk/7FE00010", buf.BulkDataUri);
        }

        [Fact]
        public void Replace_TopLevelOB_ReplacedWithBulkDataUri()
        {
            var ds = new DicomDataset
            {
                new DicomOtherByte(DicomTag.SpectroscopyData, new MemoryByteBuffer(new byte[] { 0xFF }))
            };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.SpectroscopyData);
            var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
            Assert.Equal($"{BaseUrl}/bulk/{DicomBulkDataHelper.FormatTagForUrl(DicomTag.SpectroscopyData)}", buf.BulkDataUri);
        }

        [Fact]
        public void Replace_FragmentSequence_ReplacedWithBulkDataUri()
        {
            var ds = new DicomDataset();
            var frag = new DicomOtherWordFragment(DicomTag.PixelData);
            frag.Add(new MemoryByteBuffer(new byte[] { 0x00, 0x00, 0x00, 0x00 })); // offset table
            frag.Add(new MemoryByteBuffer(new byte[] { 0xAB, 0xCD }));             // fragment
            ds.Add(frag);

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            // Fragment must be replaced by a plain DicomOtherWord backed by BulkDataUriByteBuffer.
            var elem = result.GetDicomItem<DicomElement>(DicomTag.PixelData);
            Assert.NotNull(elem);
            Assert.False(elem is DicomFragmentSequence,
                "Fragment sequence should have been replaced, not kept as-is");
            var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
            Assert.Equal($"{BaseUrl}/bulk/7FE00010", buf.BulkDataUri);
        }

        [Fact]
        public void Replace_NonBulkVR_Preserved()
        {
            var ds = new DicomDataset
            {
                new DicomLongString(DicomTag.PatientName, "Doe^John")
            };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.PatientName);
            Assert.NotNull(elem);
            Assert.False(elem.Buffer is IBulkDataUriByteBuffer);
        }

        [Fact]
        public void Replace_EmptyDataset_ReturnsEmptyDataset()
        {
            var ds = new DicomDataset();
            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);
            Assert.Empty(result);
        }

        // ── Threshold behaviour ───────────────────────────────────────────────

        [Fact]
        public void Replace_ThresholdZero_AllBulkReplaced()
        {
            var ds = new DicomDataset
            {
                new DicomOtherByte(DicomTag.SpectroscopyData, new MemoryByteBuffer(new byte[] { 0x01 }))
            };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, inlineThreshold: 0);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.SpectroscopyData);
            Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
        }

        [Fact]
        public void Replace_BelowThreshold_StaysInline()
        {
            var smallData = new byte[] { 0x01, 0x02 }; // 2 bytes
            var ds = new DicomDataset
            {
                new DicomOtherByte(DicomTag.SpectroscopyData, new MemoryByteBuffer(smallData))
            };

            // Threshold of 10 — 2 bytes is ≤ 10, so it stays inline.
            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, inlineThreshold: 10);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.SpectroscopyData);
            Assert.False(elem.Buffer is IBulkDataUriByteBuffer,
                "Small element should stay inline when size <= threshold");
        }

        [Fact]
        public void Replace_AboveThreshold_IsReplaced()
        {
            var largeData = new byte[20]; // 20 bytes
            var ds = new DicomDataset
            {
                new DicomOtherByte(DicomTag.SpectroscopyData, new MemoryByteBuffer(largeData))
            };

            // Threshold of 10 — 20 bytes > 10, so it must be replaced.
            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, inlineThreshold: 10);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.SpectroscopyData);
            Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
        }

        // ── Original dataset unmodified ───────────────────────────────────────

        [Fact]
        public void Replace_OriginalDatasetUnmodified()
        {
            var pixels = new byte[] { 1, 2, 3, 4 };
            var ds = new DicomDataset
            {
                new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixels))
            };

            DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            // Original must still have the original (non-URI) buffer.
            var elem = ds.GetDicomItem<DicomElement>(DicomTag.PixelData);
            Assert.False(elem.Buffer is IBulkDataUriByteBuffer,
                "Original dataset must not be modified");
        }

        // ── Nested sequence recursion ─────────────────────────────────────────

        [Fact]
        public void Replace_NestedSequenceOB_ReplacedWithNestedPath()
        {
            // Build: ReferencedSOPSequence (0008,1115) containing one item
            // with WaveformData (5400,1010) as an OB element.
            var item = new DicomDataset
            {
                new DicomOtherByte(DicomTag.WaveformData, new MemoryByteBuffer(new byte[] { 0xAA, 0xBB }))
            };
            var seq = new DicomSequence(DicomTag.ReferencedSOPSequence, item);
            var ds = new DicomDataset { seq };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var seqResult = result.GetDicomItem<DicomSequence>(DicomTag.ReferencedSOPSequence);
            Assert.NotNull(seqResult);
            var itemResult = seqResult.Items[0];
            var elem = itemResult.GetDicomItem<DicomElement>(DicomTag.WaveformData);
            Assert.NotNull(elem);
            var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);

            // Expected path: {base}/{seqTag}/0/{elementTag}
            // Note: no /bulk/ prefix for nested elements; the path encodes the full navigation.
            var seqTag  = DicomBulkDataHelper.FormatTagForUrl(DicomTag.ReferencedSOPSequence);
            var elemTag = DicomBulkDataHelper.FormatTagForUrl(DicomTag.WaveformData);
            Assert.Equal($"{BaseUrl}/{seqTag}/0/{elemTag}", buf.BulkDataUri);
        }

        [Fact]
        public void Replace_MultipleItemsInSequence_EachGetsCorrectIndex()
        {
            Func<byte, DicomDataset> makeItem = val => new DicomDataset
            {
                new DicomOtherByte(DicomTag.WaveformData, new MemoryByteBuffer(new byte[] { val }))
            };

            var seq = new DicomSequence(
                DicomTag.ReferencedSOPSequence,
                makeItem(0x01), makeItem(0x02), makeItem(0x03));
            var ds = new DicomDataset { seq };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var seqTag  = DicomBulkDataHelper.FormatTagForUrl(DicomTag.ReferencedSOPSequence);
            var elemTag = DicomBulkDataHelper.FormatTagForUrl(DicomTag.WaveformData);

            var seqResult = result.GetDicomItem<DicomSequence>(DicomTag.ReferencedSOPSequence);
            for (int i = 0; i < 3; i++)
            {
                var elem = seqResult.Items[i].GetDicomItem<DicomElement>(DicomTag.WaveformData);
                var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
                Assert.Equal($"{BaseUrl}/{seqTag}/{i}/{elemTag}", buf.BulkDataUri);
            }
        }

        // ── AlreadyBulkDataUri is a no-op ─────────────────────────────────────

        [Fact]
        public void Replace_AlreadyBulkDataUri_LeftUnchanged()
        {
            var existingUri = "http://pacs.example.com/bulk/123";
            var ds = new DicomDataset
            {
                new DicomOtherByte(DicomTag.SpectroscopyData, new BulkDataUriByteBuffer(existingUri))
            };

            var result = DicomBulkDataHelper.ReplaceBulkDataWithUris(ds, BaseUrl, 0);

            var elem = result.GetDicomItem<DicomElement>(DicomTag.SpectroscopyData);
            var buf = Assert.IsAssignableFrom<IBulkDataUriByteBuffer>(elem.Buffer);
            Assert.Equal(existingUri, buf.BulkDataUri);
        }
    }
}

#endif
