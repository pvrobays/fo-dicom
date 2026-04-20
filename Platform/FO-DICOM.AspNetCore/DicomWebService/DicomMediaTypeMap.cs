// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Maps <see cref="DicomTransferSyntax"/> UIDs to the MIME media types used in WADO-RS
    /// frame retrieval responses (PS3.18 Section 10.4.1.1.3).
    /// <para>
    /// The standard specifies a per-transfer-syntax MIME type for the
    /// <c>Content-Type</c> of each multipart part. For uncompressed syntaxes (and for any
    /// syntax not explicitly listed) the type is <c>application/octet-stream</c>.
    /// </para>
    /// </summary>
    internal static class DicomMediaTypeMap
    {
        // ── MIME type constants ───────────────────────────────────────────────

        /// <summary>Raw uncompressed pixel data (PS3.18 default for frame retrieval).</summary>
        internal const string OctetStream = "application/octet-stream";

        /// <summary>JPEG (all Process variants: Baseline, Extended, Lossless).</summary>
        internal const string ImageJpeg = "image/jpeg";

        /// <summary>JPEG-LS (lossless and near-lossless).</summary>
        internal const string ImageJls = "image/x-jls";

        /// <summary>JPEG 2000 (Part 1: lossless and lossy).</summary>
        internal const string ImageJp2 = "image/jp2";

        /// <summary>JPEG 2000 Part 2 Multi-component.</summary>
        internal const string ImageJpx = "image/jpx";

        /// <summary>High-Throughput JPEG 2000 (HTJ2K, all variants).</summary>
        internal const string ImageJphc = "image/jphc";

        /// <summary>RLE Lossless.</summary>
        internal const string ImageDicomRle = "image/dicom-rle";

        /// <summary>MPEG-2 video.</summary>
        internal const string VideoMpeg2 = "video/mpeg2";

        /// <summary>MPEG-4 / H.264 / H.265 video.</summary>
        internal const string VideoMp4 = "video/mp4";

        // ── UID → MIME type lookup ────────────────────────────────────────────

        private static readonly Dictionary<string, string> _uidToMimeType
            = new Dictionary<string, string>
        {
            // ── JPEG (non-retired, active processes) ────────────────────────────
            // PS3.18 Table 10.4.3-1
            { DicomTransferSyntax.JPEGProcess1.UID.UID,                    ImageJpeg },
            { DicomTransferSyntax.JPEGProcess2_4.UID.UID,                  ImageJpeg },
            { DicomTransferSyntax.JPEGProcess14.UID.UID,                   ImageJpeg },
            { DicomTransferSyntax.JPEGProcess14SV1.UID.UID,                ImageJpeg },

            // Retired JPEG processes — still map to image/jpeg per PS3.18
            { DicomTransferSyntax.JPEGProcess3_5Retired.UID.UID,           ImageJpeg },
            { DicomTransferSyntax.JPEGProcess6_8Retired.UID.UID,           ImageJpeg },
            { DicomTransferSyntax.JPEGProcess7_9Retired.UID.UID,           ImageJpeg },
            { DicomTransferSyntax.JPEGProcess10_12Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess11_13Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess15Retired.UID.UID,            ImageJpeg },
            { DicomTransferSyntax.JPEGProcess16_18Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess17_19Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess20_22Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess21_23Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess24_26Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess25_27Retired.UID.UID,         ImageJpeg },
            { DicomTransferSyntax.JPEGProcess28Retired.UID.UID,            ImageJpeg },
            { DicomTransferSyntax.JPEGProcess29Retired.UID.UID,            ImageJpeg },

            // ── JPEG-LS ─────────────────────────────────────────────────────────
            { DicomTransferSyntax.JPEGLSLossless.UID.UID,                  ImageJls  },
            { DicomTransferSyntax.JPEGLSNearLossless.UID.UID,              ImageJls  },

            // ── JPEG 2000 (Part 1) ───────────────────────────────────────────────
            { DicomTransferSyntax.JPEG2000Lossless.UID.UID,                ImageJp2  },
            { DicomTransferSyntax.JPEG2000Lossy.UID.UID,                   ImageJp2  },

            // ── JPEG 2000 (Part 2 Multi-component) ──────────────────────────────
            { DicomTransferSyntax.JPEG2000Part2MultiComponentLosslessOnly.UID.UID, ImageJpx },
            { DicomTransferSyntax.JPEG2000Part2MultiComponent.UID.UID,     ImageJpx  },

            // ── High-Throughput JPEG 2000 (HTJ2K) ───────────────────────────────
            { DicomTransferSyntax.HTJ2KLossless.UID.UID,                   ImageJphc },
            { DicomTransferSyntax.HTJ2KLosslessRPCL.UID.UID,               ImageJphc },
            { DicomTransferSyntax.HTJ2K.UID.UID,                           ImageJphc },

            // ── RLE Lossless ─────────────────────────────────────────────────────
            { DicomTransferSyntax.RLELossless.UID.UID,                     ImageDicomRle },

            // ── MPEG-2 ──────────────────────────────────────────────────────────
            { DicomTransferSyntax.MPEG2.UID.UID,                           VideoMpeg2 },
            { DicomTransferSyntax.MPEG2MainProfileHighLevel.UID.UID,       VideoMpeg2 },
            { DicomTransferSyntax.FragmentableMPEG2.UID.UID,               VideoMpeg2 },
            { DicomTransferSyntax.FragmentableMPEG2MainProfileHighLevel.UID.UID, VideoMpeg2 },

            // ── MPEG-4 / H.264 ──────────────────────────────────────────────────
            { DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41.UID.UID,  VideoMp4  },
            { DicomTransferSyntax.FragmentableMPEG4AVCH264HighProfileLevel41.UID.UID, VideoMp4 },
            { DicomTransferSyntax.MPEG4AVCH264BDCompatibleHighProfileLevel41.UID.UID, VideoMp4 },
            { DicomTransferSyntax.FragmentableMPEG4AVCH264BDCompatibleHighProfileLevel41.UID.UID, VideoMp4 },
            { DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For2DVideo.UID.UID,   VideoMp4 },
            { DicomTransferSyntax.FragmentableMPEG4AVCH264HighProfileLevel42For2DVideo.UID.UID,   VideoMp4 },
            { DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For3DVideo.UID.UID,   VideoMp4 },
            { DicomTransferSyntax.FragmentableMPEG4AVCH264HighProfileLevel42For3DVideo.UID.UID,   VideoMp4 },
            { DicomTransferSyntax.MPEG4AVCH264StereoHighProfileLevel42.UID.UID,       VideoMp4 },
            { DicomTransferSyntax.FragmentableMPEG4AVCH264StereoHighProfileLevel42.UID.UID,       VideoMp4 },

            // ── H.265 / HEVC ─────────────────────────────────────────────────────
            { DicomTransferSyntax.HEVCH265MainProfileLevel51.UID.UID,      VideoMp4  },
            { DicomTransferSyntax.HEVCH265Main10ProfileLevel51.UID.UID,    VideoMp4  },
        };

        // ── MIME type → set of matching UIDs (for Accept negotiation) ─────────

        /// <summary>
        /// All MIME types that are valid for WADO-RS frame retrieval Accept headers,
        /// mapped to the representative set of active (non-retired) transfer syntax UIDs
        /// that produce that media type.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string[]> MimeTypeToUids
            = new Dictionary<string, string[]>
        {
            { OctetStream,    new string[0] /* fallback; all uncompressed syntaxes */ },
            { ImageJpeg,      new[] { DicomTransferSyntax.JPEGProcess1.UID.UID,
                                      DicomTransferSyntax.JPEGProcess2_4.UID.UID,
                                      DicomTransferSyntax.JPEGProcess14.UID.UID,
                                      DicomTransferSyntax.JPEGProcess14SV1.UID.UID } },
            { ImageJls,       new[] { DicomTransferSyntax.JPEGLSLossless.UID.UID,
                                      DicomTransferSyntax.JPEGLSNearLossless.UID.UID } },
            { ImageJp2,       new[] { DicomTransferSyntax.JPEG2000Lossless.UID.UID,
                                      DicomTransferSyntax.JPEG2000Lossy.UID.UID } },
            { ImageJpx,       new[] { DicomTransferSyntax.JPEG2000Part2MultiComponentLosslessOnly.UID.UID,
                                      DicomTransferSyntax.JPEG2000Part2MultiComponent.UID.UID } },
            { ImageJphc,      new[] { DicomTransferSyntax.HTJ2KLossless.UID.UID,
                                      DicomTransferSyntax.HTJ2KLosslessRPCL.UID.UID,
                                      DicomTransferSyntax.HTJ2K.UID.UID } },
            { ImageDicomRle,  new[] { DicomTransferSyntax.RLELossless.UID.UID } },
            { VideoMpeg2,     new[] { DicomTransferSyntax.MPEG2.UID.UID,
                                      DicomTransferSyntax.MPEG2MainProfileHighLevel.UID.UID } },
            { VideoMp4,       new[] { DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41.UID.UID,
                                      DicomTransferSyntax.MPEG4AVCH264BDCompatibleHighProfileLevel41.UID.UID,
                                      DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For2DVideo.UID.UID,
                                      DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For3DVideo.UID.UID,
                                      DicomTransferSyntax.MPEG4AVCH264StereoHighProfileLevel42.UID.UID,
                                      DicomTransferSyntax.HEVCH265MainProfileLevel51.UID.UID,
                                      DicomTransferSyntax.HEVCH265Main10ProfileLevel51.UID.UID } },
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the MIME media type for the given <paramref name="transferSyntax"/>,
        /// or <see cref="OctetStream"/> when the syntax is uncompressed or not in the table.
        /// </summary>
        internal static string GetMimeType(DicomTransferSyntax transferSyntax)
        {
            if (transferSyntax == null) return OctetStream;
            if (_uidToMimeType.TryGetValue(transferSyntax.UID.UID, out var mime)) return mime;
            return OctetStream;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="mimeType"/> is a recognised WADO-RS
        /// frame media type (case-insensitive match).
        /// </summary>
        internal static bool IsKnownFrameMimeType(string mimeType)
            => MimeTypeToUids.ContainsKey(mimeType?.ToLowerInvariant() ?? string.Empty);
    }
}
