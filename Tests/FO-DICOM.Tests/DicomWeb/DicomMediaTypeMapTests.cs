// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomMediaTypeMapTests
    {
        // ── GetMimeType — uncompressed syntaxes ──────────────────────────────

        [FactForNetCore]
        public void GetMimeType_ImplicitVRLittleEndian_ReturnsOctetStream()
            => Assert.Equal(DicomMediaTypeMap.OctetStream,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.ImplicitVRLittleEndian));

        [FactForNetCore]
        public void GetMimeType_ExplicitVRLittleEndian_ReturnsOctetStream()
            => Assert.Equal(DicomMediaTypeMap.OctetStream,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.ExplicitVRLittleEndian));

        [FactForNetCore]
        public void GetMimeType_ExplicitVRBigEndian_ReturnsOctetStream()
            => Assert.Equal(DicomMediaTypeMap.OctetStream,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.ExplicitVRBigEndian));

        [FactForNetCore]
        public void GetMimeType_Null_ReturnsOctetStream()
            => Assert.Equal(DicomMediaTypeMap.OctetStream, DicomMediaTypeMap.GetMimeType(null));

        // ── GetMimeType — JPEG syntaxes ──────────────────────────────────────

        [FactForNetCore]
        public void GetMimeType_JPEGProcess1_ReturnsImageJpeg()
            => Assert.Equal(DicomMediaTypeMap.ImageJpeg,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGProcess1));

        [FactForNetCore]
        public void GetMimeType_JPEGProcess2_4_ReturnsImageJpeg()
            => Assert.Equal(DicomMediaTypeMap.ImageJpeg,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGProcess2_4));

        [FactForNetCore]
        public void GetMimeType_JPEGProcess14_ReturnsImageJpeg()
            => Assert.Equal(DicomMediaTypeMap.ImageJpeg,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGProcess14));

        [FactForNetCore]
        public void GetMimeType_JPEGProcess14SV1_ReturnsImageJpeg()
            => Assert.Equal(DicomMediaTypeMap.ImageJpeg,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGProcess14SV1));

        // ── GetMimeType — JPEG-LS syntaxes ───────────────────────────────────

        [FactForNetCore]
        public void GetMimeType_JPEGLSLossless_ReturnsImageJls()
            => Assert.Equal(DicomMediaTypeMap.ImageJls,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGLSLossless));

        [FactForNetCore]
        public void GetMimeType_JPEGLSNearLossless_ReturnsImageJls()
            => Assert.Equal(DicomMediaTypeMap.ImageJls,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEGLSNearLossless));

        // ── GetMimeType — JPEG 2000 syntaxes ─────────────────────────────────

        [FactForNetCore]
        public void GetMimeType_JPEG2000Lossless_ReturnsImageJp2()
            => Assert.Equal(DicomMediaTypeMap.ImageJp2,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEG2000Lossless));

        [FactForNetCore]
        public void GetMimeType_JPEG2000Lossy_ReturnsImageJp2()
            => Assert.Equal(DicomMediaTypeMap.ImageJp2,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEG2000Lossy));

        [FactForNetCore]
        public void GetMimeType_JPEG2000Part2MCLossless_ReturnsImageJpx()
            => Assert.Equal(DicomMediaTypeMap.ImageJpx,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEG2000Part2MultiComponentLosslessOnly));

        [FactForNetCore]
        public void GetMimeType_JPEG2000Part2MC_ReturnsImageJpx()
            => Assert.Equal(DicomMediaTypeMap.ImageJpx,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.JPEG2000Part2MultiComponent));

        // ── GetMimeType — HTJ2K syntaxes ─────────────────────────────────────

        [FactForNetCore]
        public void GetMimeType_HTJ2KLossless_ReturnsImageJphc()
            => Assert.Equal(DicomMediaTypeMap.ImageJphc,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.HTJ2KLossless));

        [FactForNetCore]
        public void GetMimeType_HTJ2KLosslessRPCL_ReturnsImageJphc()
            => Assert.Equal(DicomMediaTypeMap.ImageJphc,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.HTJ2KLosslessRPCL));

        [FactForNetCore]
        public void GetMimeType_HTJ2K_ReturnsImageJphc()
            => Assert.Equal(DicomMediaTypeMap.ImageJphc,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.HTJ2K));

        // ── GetMimeType — RLE ─────────────────────────────────────────────────

        [FactForNetCore]
        public void GetMimeType_RLELossless_ReturnsImageDicomRle()
            => Assert.Equal(DicomMediaTypeMap.ImageDicomRle,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.RLELossless));

        // ── GetMimeType — MPEG / H.264 / H.265 ───────────────────────────────

        [FactForNetCore]
        public void GetMimeType_MPEG2_ReturnsVideoMpeg2()
            => Assert.Equal(DicomMediaTypeMap.VideoMpeg2,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.MPEG2));

        [FactForNetCore]
        public void GetMimeType_MPEG4H264_ReturnsVideoMp4()
            => Assert.Equal(DicomMediaTypeMap.VideoMp4,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41));

        [FactForNetCore]
        public void GetMimeType_HEVC_ReturnsVideoMp4()
            => Assert.Equal(DicomMediaTypeMap.VideoMp4,
                DicomMediaTypeMap.GetMimeType(DicomTransferSyntax.HEVCH265MainProfileLevel51));

        // ── IsKnownFrameMimeType ──────────────────────────────────────────────

        [FactForNetCore]
        public void IsKnownFrameMimeType_OctetStream_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("application/octet-stream"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageJpeg_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/jpeg"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageJls_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/x-jls"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageJp2_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/jp2"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageJpx_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/jpx"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageJphc_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/jphc"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ImageDicomRle_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("image/dicom-rle"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_VideoMpeg2_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("video/mpeg2"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_VideoMp4_ReturnsTrue()
            => Assert.True(DicomMediaTypeMap.IsKnownFrameMimeType("video/mp4"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_TextHtml_ReturnsFalse()
            => Assert.False(DicomMediaTypeMap.IsKnownFrameMimeType("text/html"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_ApplicationDicom_ReturnsFalse()
            => Assert.False(DicomMediaTypeMap.IsKnownFrameMimeType("application/dicom"));

        [FactForNetCore]
        public void IsKnownFrameMimeType_Null_ReturnsFalse()
            => Assert.False(DicomMediaTypeMap.IsKnownFrameMimeType(null));
    }
}

#endif
