// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.AspNetCore;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Imaging.Codec;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    // ── Content-Location mode ─────────────────────────────────────────────────

    /// <summary>
    /// Controls how <c>Content-Location</c> headers are formatted in WADO-RS multipart responses
    /// (PS3.18 Section 10.4.1.1).
    /// </summary>
    public enum ContentLocationMode
    {
        /// <summary>
        /// No <c>Content-Location</c> header is emitted. Use this to suppress the header
        /// entirely, e.g. for internal or legacy clients that don't need it.
        /// </summary>
        None,

        /// <summary>
        /// Emit a path-absolute <c>Content-Location</c> value, e.g.
        /// <c>/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9</c>.
        /// Works correctly behind reverse proxies regardless of the external hostname.
        /// This is the default.
        /// </summary>
        Relative,

        /// <summary>
        /// Emit a fully-qualified absolute URL, e.g.
        /// <c>https://pacs.example.com/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9</c>.
        /// Uses <c>HttpRequest.Scheme</c> and <c>HttpRequest.Host</c>; configure
        /// <c>ForwardedHeaders</c> middleware when running behind a reverse proxy.
        /// </summary>
        Absolute,
    }

    // ── Content negotiation result for WADO-RS instance retrieval ────────────

    /// <summary>
    /// The result of <c>Accept</c> header negotiation for a WADO-RS instance retrieval request.
    /// </summary>
    internal readonly struct WadoInstanceNegotiationResult
    {
        /// <summary>
        /// <c>false</c> when none of the client's acceptable media types are supported;
        /// the service should return HTTP 406 Not Acceptable.
        /// </summary>
        internal bool IsAcceptable { get; }

        /// <summary>
        /// The specific transfer syntax parsed from <c>transfer-syntax=&lt;uid&gt;</c> in the
        /// <c>Accept</c> header, or <c>null</c> when the client sent <c>transfer-syntax=*</c>
        /// or omitted the parameter entirely.
        /// </summary>
        internal DicomTransferSyntax? RequestedTransferSyntax { get; }

        /// <summary>
        /// <c>true</c> when the client sent <c>transfer-syntax=*</c>, meaning it will accept
        /// any transfer syntax and no transcoding should be performed.
        /// </summary>
        internal bool AcceptsAnyTransferSyntax { get; }

        internal WadoInstanceNegotiationResult(
            bool isAcceptable,
            DicomTransferSyntax? requestedTransferSyntax,
            bool acceptsAnyTransferSyntax)
        {
            IsAcceptable = isAcceptable;
            RequestedTransferSyntax = requestedTransferSyntax;
            AcceptsAnyTransferSyntax = acceptsAnyTransferSyntax;
        }

        /// <summary>A pre-built result for HTTP 406 Not Acceptable.</summary>
        internal static WadoInstanceNegotiationResult NotAcceptable
            => new WadoInstanceNegotiationResult(false, null, false);

        /// <summary>
        /// A pre-built result meaning "accept any transfer syntax" (no transcoding).
        /// Used for <c>transfer-syntax=*</c> and pragmatic defaults.
        /// </summary>
        internal static WadoInstanceNegotiationResult AnyTransferSyntax
            => new WadoInstanceNegotiationResult(true, null, true);

        /// <summary>
        /// A pre-built result meaning "use the default transfer syntax"
        /// (Explicit VR Little Endian, per PS3.18 Section 8.7.3).
        /// </summary>
        internal static WadoInstanceNegotiationResult Default
            => new WadoInstanceNegotiationResult(true, DicomTransferSyntax.ExplicitVRLittleEndian, false);
    }

    /// <summary>
    /// Translates an <see cref="IDicomWadoResponse"/> into an HTTP response, handling content
    /// negotiation, multipart serialization of DICOM instances and metadata, and error mapping.
    /// <para>
    /// Instances are reusable across requests — create one per <see cref="DicomWebService"/>
    /// configuration and retain it for the lifetime of the service.
    /// </para>
    /// </summary>
    internal class WadoResponseWriter
    {
        private readonly string _serviceAgent;
        private readonly bool _writeTagsAsKeywords;
        private readonly bool _formatJsonIndented;
        private readonly ContentLocationMode _contentLocationMode;

        internal WadoResponseWriter(
            string serviceAgent,
            bool writeTagsAsKeywords,
            bool formatJsonIndented,
            ContentLocationMode contentLocationMode = ContentLocationMode.Relative)
        {
            _serviceAgent = serviceAgent;
            _writeTagsAsKeywords = writeTagsAsKeywords;
            _formatJsonIndented = formatJsonIndented;
            _contentLocationMode = contentLocationMode;
        }

        // ── Accept header negotiation for instance retrieval ──────────────────

        /// <summary>
        /// Parses the HTTP <c>Accept</c> header to determine the requested transfer syntax
        /// for a WADO-RS instance retrieval request (PS3.18 Section 8.7).
        /// <list type="bullet">
        ///   <item>Missing / empty / <c>*/*</c> → pragmatic default (Explicit VR Little Endian).
        ///     <c>*/*</c> is treated as default regardless of position in the list; quality-factor
        ///     negotiation (RFC 7231) is not implemented.</item>
        ///   <item><c>multipart/related; type="application/dicom"</c> with no <c>transfer-syntax</c> → default (Explicit VR LE)</item>
        ///   <item><c>... transfer-syntax=*</c> → accept any transfer syntax (no transcoding)</item>
        ///   <item><c>... transfer-syntax=&lt;uid&gt;</c> → specific transfer syntax requested</item>
        ///   <item>Any other media type → 406 Not Acceptable</item>
        /// </list>
        /// </summary>
        internal static WadoInstanceNegotiationResult NegotiateInstanceFormat(HttpContext context)
        {
            var acceptHeader = context.Request.Headers["Accept"].ToString();

            // Missing / empty or wildcard → pragmatic default (Explicit VR LE)
            if (string.IsNullOrWhiteSpace(acceptHeader) || acceptHeader.Contains("*/*"))
            {
                return WadoInstanceNegotiationResult.Default;
            }

            // Must contain application/dicom (the bare WADO-RS media type, not +json or +xml).
            // Scan for the token and verify that the character immediately following it is NOT
            // '+' — this prevents matching application/dicom+json or application/dicom+xml.
            int searchFrom = 0;
            int dicomIdx = -1;
            const string dicomToken = "application/dicom";
            while (true)
            {
                int idx = acceptHeader.IndexOf(dicomToken, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                int afterToken = idx + dicomToken.Length;
                // Only accept this match if the token is followed by a word boundary
                // (end-of-string, whitespace, ';', ',', or '"') — not '+'.
                if (afterToken >= acceptHeader.Length ||
                    acceptHeader[afterToken] == ' ' || acceptHeader[afterToken] == '\t' ||
                    acceptHeader[afterToken] == ';' || acceptHeader[afterToken] == ',' ||
                    acceptHeader[afterToken] == '"')
                {
                    dicomIdx = idx;
                    break;
                }

                // This occurrence was application/dicom+something — skip past it and keep looking.
                searchFrom = afterToken;
            }

            if (dicomIdx < 0)
            {
                return WadoInstanceNegotiationResult.NotAcceptable;
            }

            // Look for transfer-syntax parameter after the application/dicom token.
            int tsCandidateStart = dicomIdx + dicomToken.Length;
            string remainder = acceptHeader.Substring(tsCandidateStart);

            // Find transfer-syntax= parameter (case-insensitive)
            int tsParamIdx = remainder.IndexOf("transfer-syntax", StringComparison.OrdinalIgnoreCase);

            if (tsParamIdx < 0)
            {
                // application/dicom present but no transfer-syntax parameter → default
                return WadoInstanceNegotiationResult.Default;
            }

            // Advance past "transfer-syntax" and optional whitespace / '='
            int afterKey = tsParamIdx + "transfer-syntax".Length;
            while (afterKey < remainder.Length && (remainder[afterKey] == ' ' || remainder[afterKey] == '\t'))
            {
                afterKey++;
            }

            if (afterKey >= remainder.Length || remainder[afterKey] != '=')
            {
                // Malformed parameter — treat as default
                return WadoInstanceNegotiationResult.Default;
            }

            afterKey++; // skip '='

            // Skip any whitespace after '='
            while (afterKey < remainder.Length && (remainder[afterKey] == ' ' || remainder[afterKey] == '\t'))
            {
                afterKey++;
            }

            // Read the value until semicolon, comma, or end (strip optional quotes)
            int valueStart = afterKey;
            if (valueStart >= remainder.Length)
            {
                return WadoInstanceNegotiationResult.Default;
            }

            bool quoted = remainder[valueStart] == '"';
            if (quoted) valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < remainder.Length)
            {
                char c = remainder[valueEnd];
                if (quoted ? c == '"' : (c == ';' || c == ',' || c == ' ' || c == '\t'))
                {
                    break;
                }
                valueEnd++;
            }

            string tsValue = remainder.Substring(valueStart, valueEnd - valueStart).Trim();

            if (tsValue == "*")
            {
                return WadoInstanceNegotiationResult.AnyTransferSyntax;
            }

            if (string.IsNullOrEmpty(tsValue))
            {
                return WadoInstanceNegotiationResult.Default;
            }

            // Parse the UID
            try
            {
                var ts = DicomTransferSyntax.Parse(tsValue);
                return new WadoInstanceNegotiationResult(true, ts, false);
            }
            catch
            {
                // Unknown / unparseable UID — treat as not acceptable
                return WadoInstanceNegotiationResult.NotAcceptable;
            }
        }

        // ── Instance retrieval ────────────────────────────────────────────────

        /// <summary>
        /// Writes the HTTP response for a WADO-RS instance retrieval request.
        /// <para>
        /// When <paramref name="negotiation"/> specifies a particular transfer syntax,
        /// <see cref="DicomFile"/>-based responses are automatically transcoded to match.
        /// For list responses, if any file cannot be transcoded the response is set to
        /// 406 Not Acceptable before any data is written. For async-streaming responses
        /// a failed transcode falls back to the original transfer syntax and a
        /// <c>Warning: 299</c> header is emitted.
        /// </para>
        /// <para>
        /// Raw byte-stream responses (<see cref="DicomWadoRawInstancesResponse"/> /
        /// <see cref="DicomWadoAsyncRawInstancesResponse"/>) are written verbatim —
        /// the provider is assumed to have already encoded them correctly.
        /// </para>
        /// Success responses are written as <c>multipart/related; type="application/dicom"</c>
        /// (PS3.18 Table 10.4.4-1).
        /// </summary>
        internal async Task WriteInstancesAsync(
            HttpContext context,
            IDicomWadoInstanceResponse response,
            WadoInstanceNegotiationResult negotiation,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWadoInstancesResponse instancesResponse:
                    var targetSyntax = ResolveTargetSyntax(negotiation);
                    if (targetSyntax != null)
                    {
                        var transcoded = TryTranscodeAll(instancesResponse.Results, targetSyntax, context);
                        if (transcoded == null)
                        {
                            return; // 406 already written
                        }
                        await WriteDicomMultipartAsync(context, EnumerateFilesAsync(transcoded),
                            targetSyntax, cancellationToken);
                    }
                    else
                    {
                        await WriteDicomMultipartAsync(context, EnumerateFilesAsync(instancesResponse.Results),
                            null, cancellationToken);
                    }
                    break;

                case DicomWadoRawInstancesResponse rawResponse:
                    await WriteRawMultipartAsync(context,
                        EnumerateRawAsync(rawResponse.Results), cancellationToken);
                    break;

                case DicomWadoAsyncInstancesResponse asyncResponse:
                    var asyncTarget = ResolveTargetSyntax(negotiation);
                    await WriteDicomMultipartAsync(context,
                        asyncTarget != null
                            ? TranscodeStreamingAsync(asyncResponse.Results, asyncTarget, context, cancellationToken)
                            : asyncResponse.Results,
                        asyncTarget, cancellationToken);
                    break;

                case DicomWadoAsyncRawInstancesResponse asyncRawResponse:
                    await WriteRawMultipartAsync(context, asyncRawResponse.Results, cancellationToken);
                    break;

                default:
                    await WriteFailureAsync(context, response, cancellationToken);
                    break;
            }
        }

        /// <summary>
        /// Returns the target <see cref="DicomTransferSyntax"/> to use for transcoding, or
        /// <c>null</c> when the instances should be returned in their original transfer syntax
        /// (i.e., <c>transfer-syntax=*</c> was requested).
        /// </summary>
        private static DicomTransferSyntax? ResolveTargetSyntax(WadoInstanceNegotiationResult negotiation)
        {
            if (negotiation.AcceptsAnyTransferSyntax)
            {
                return null; // no transcoding
            }

            // Specific syntax requested, or default (Explicit VR LE)
            return negotiation.RequestedTransferSyntax ?? DicomTransferSyntax.ExplicitVRLittleEndian;
        }

        /// <summary>
        /// Attempts to transcode all files in <paramref name="files"/> to
        /// <paramref name="targetSyntax"/>. Returns the transcoded list on success, or
        /// <c>null</c> if any file cannot be transcoded (in which case a 406 response has
        /// already been written to <paramref name="context"/>).
        /// </summary>
        private static IList<DicomFile>? TryTranscodeAll(
            IList<DicomFile> files,
            DicomTransferSyntax targetSyntax,
            HttpContext context)
        {
            var result = new List<DicomFile>(files.Count);
            foreach (var file in files)
            {
                var currentSyntax = file.FileMetaInfo?.TransferSyntax ?? file.Dataset.InternalTransferSyntax;
                if (currentSyntax == targetSyntax)
                {
                    result.Add(file);
                    continue;
                }

                try
                {
                    result.Add(file.Clone(targetSyntax));
                }
                catch
                {
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    return null;
                }
            }

            return result;
        }

        /// <summary>
        /// Transcodes each file in the async stream to <paramref name="targetSyntax"/>.
        /// On failure, emits a <c>Warning: 299</c> header (if headers haven't been sent yet)
        /// and falls back to the original file.
        /// </summary>
#pragma warning disable CS1998 // async without await — yield-based
        private async IAsyncEnumerable<DicomFile> TranscodeStreamingAsync(
            IAsyncEnumerable<DicomFile> source,
            DicomTransferSyntax targetSyntax,
            HttpContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var file in source.WithCancellation(cancellationToken))
            {
                var currentSyntax = file.FileMetaInfo?.TransferSyntax ?? file.Dataset.InternalTransferSyntax;
                if (currentSyntax == targetSyntax)
                {
                    yield return file;
                    continue;
                }

                DicomFile transcoded;
                try
                {
                    transcoded = file.Clone(targetSyntax);
                }
                catch
                {
                    // Already committed to 200 — fall back to original and warn
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Headers.Append("Warning",
                            $"299 {_serviceAgent} \"The requested transfer syntax could not be applied to one or more instances; original transfer syntax used.\"");
                    }
                    transcoded = file;
                }

                yield return transcoded;
            }
        }
#pragma warning restore CS1998

        // ── Metadata retrieval ────────────────────────────────────────────────

        /// <summary>
        /// Writes the HTTP response for a WADO-RS metadata retrieval request.
        /// Success responses use the same content negotiation as QIDO-RS metadata:
        /// <c>application/dicom+json</c> (default) or
        /// <c>multipart/related; type="application/dicom+xml"</c> (PS3.18 Table 10.4.4-1).
        /// </summary>
        internal async Task WriteMetadataAsync(
            HttpContext context,
            IDicomWadoMetadataResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWadoMetadataResponse metadataResponse:
                    await WriteMetadataListAsync(context, metadataResponse.Results, cancellationToken);
                    break;

                case DicomWadoAsyncMetadataResponse asyncMetadataResponse:
                    var format = NegotiateMetadataFormat(context);
                    switch (format)
                    {
                        case QidoResponseFormat.Json:
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await DicomMetadataSerializer.WriteJsonStreamingAsync(
                                context, asyncMetadataResponse.Results,
                                _writeTagsAsKeywords, _formatJsonIndented, cancellationToken);
                            break;

                        case QidoResponseFormat.Xml:
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await DicomMetadataSerializer.WriteXmlMultipartStreamingAsync(
                                context, asyncMetadataResponse.Results, cancellationToken);
                            break;

                        case QidoResponseFormat.NotAcceptable:
                            context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(nameof(format));
                    }
                    break;

                default:
                    await WriteFailureAsync(context, response, cancellationToken);
                    break;
            }
        }

        // ── Content negotiation ───────────────────────────────────────────────

        /// <summary>
        /// Determines the preferred metadata response format from the HTTP <c>Accept</c> header.
        /// Applies the same rules as QIDO-RS metadata negotiation.
        /// </summary>
        internal static QidoResponseFormat NegotiateMetadataFormat(HttpContext context)
            => QidoResponseWriter.NegotiateResponseFormat(context);

        // ── Multipart DICOM instance writing ─────────────────────────────────

        /// <summary>
        /// Writes each DICOM file as a multipart part with
        /// <c>Content-Type: application/dicom; transfer-syntax=&lt;uid&gt;</c> and, when UID
        /// information is available and <see cref="_contentLocationMode"/> is not
        /// <see cref="ContentLocationMode.None"/>, a <c>Content-Location</c> header pointing to
        /// the single-instance WADO-RS URL for that part (PS3.18 Section 10.4.1.1).
        /// When <paramref name="transferSyntax"/> is <c>null</c> the transfer syntax is read
        /// from each file's own metadata and included in the Content-Type header.
        /// </summary>
        private async Task WriteDicomMultipartAsync(
            HttpContext context,
            IAsyncEnumerable<DicomFile> files,
            DicomTransferSyntax? transferSyntax,
            CancellationToken cancellationToken)
        {
            var boundary = $"----dicom-boundary-{Guid.NewGuid():N}";
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom\"; boundary={boundary}";

            await foreach (var file in files.WithCancellation(cancellationToken))
            {
                // Determine the actual transfer syntax for this part
                var partSyntax = transferSyntax
                    ?? file.FileMetaInfo?.TransferSyntax
                    ?? file.Dataset.InternalTransferSyntax;

                // Extract UIDs for Content-Location — tolerate missing tags gracefully
                var studyUid = file.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);
                var seriesUid = file.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
                var sopUid = file.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty);
                var contentLocation = BuildContentLocation(context, studyUid, seriesUid, sopUid);

                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync(
                    $"Content-Type: application/dicom; transfer-syntax={partSyntax.UID.UID}\r\n",
                    cancellationToken);
                if (contentLocation != null)
                {
                    await context.Response.WriteAsync(
                        $"Content-Location: {contentLocation}\r\n", cancellationToken);
                }
                await context.Response.WriteAsync("\r\n", cancellationToken);

                // DicomFile.SaveAsync writes forward-only; no intermediate MemoryStream needed.
                await file.SaveAsync(context.Response.Body);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }

        private async Task WriteRawMultipartAsync(
            HttpContext context,
            IAsyncEnumerable<DicomWadoRawInstance> parts,
            CancellationToken cancellationToken)
        {
            var boundary = $"----dicom-boundary-{Guid.NewGuid():N}";
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom\"; boundary={boundary}";

            await foreach (var part in parts.WithCancellation(cancellationToken))
            {
                var contentLocation = BuildContentLocation(
                    context, part.StudyInstanceUid, part.SeriesInstanceUid, part.SopInstanceUid);

                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                if (part.TransferSyntaxUid != null)
                {
                    await context.Response.WriteAsync(
                        $"Content-Type: application/dicom; transfer-syntax={part.TransferSyntaxUid}\r\n",
                        cancellationToken);
                }
                else
                {
                    await context.Response.WriteAsync("Content-Type: application/dicom\r\n", cancellationToken);
                }
                if (contentLocation != null)
                {
                    await context.Response.WriteAsync(
                        $"Content-Location: {contentLocation}\r\n", cancellationToken);
                }
                await context.Response.WriteAsync("\r\n", cancellationToken);

                await part.Data.CopyToAsync(context.Response.Body, 81920, cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }

        // ── Content-Location helper ───────────────────────────────────────────

        /// <summary>
        /// Builds a <c>Content-Location</c> value for a single DICOM instance multipart part,
        /// or returns <c>null</c> when the value cannot be built (mode is
        /// <see cref="ContentLocationMode.None"/>, any UID is missing, or no URL prefix was found
        /// in the endpoint metadata).
        /// <para>
        /// The DICOMweb URL prefix (e.g. <c>"/dicomweb"</c>) is read directly from the
        /// <see cref="DicomWebEndpointMetadata"/> attached to <paramref name="context"/>'s
        /// current endpoint, removing the need to thread it through the call chain.
        /// </para>
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="studyUid">Study Instance UID, or <c>null</c>.</param>
        /// <param name="seriesUid">Series Instance UID, or <c>null</c>.</param>
        /// <param name="sopUid">SOP Instance UID, or <c>null</c>.</param>
        private string? BuildContentLocation(
            HttpContext context,
            string? studyUid,
            string? seriesUid,
            string? sopUid)
        {
            if (_contentLocationMode == ContentLocationMode.None) return null;
            if (string.IsNullOrEmpty(studyUid) ||
                string.IsNullOrEmpty(seriesUid) ||
                string.IsNullOrEmpty(sopUid)) return null;
            var urlPrefix = context.GetEndpoint()?.Metadata
                .GetMetadata<DicomWebEndpointMetadata>()?.UrlPrefix;
            if (urlPrefix == null) return null;

            var path = $"{urlPrefix}/studies/{studyUid}/series/{seriesUid}/instances/{sopUid}";

            if (_contentLocationMode == ContentLocationMode.Absolute)
            {
                return $"{context.Request.Scheme}://{context.Request.Host}{path}";
            }

            // Relative (path-absolute)
            return path;
        }

        // ── Metadata writing ──────────────────────────────────────────────────

        private async Task WriteMetadataListAsync(
            HttpContext context,
            IList<DicomDataset> datasets,
            CancellationToken cancellationToken)
        {
            var format = NegotiateMetadataFormat(context);
            switch (format)
            {
                case QidoResponseFormat.Json:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await DicomMetadataSerializer.WriteJsonAsync(
                        context, datasets, _writeTagsAsKeywords, _formatJsonIndented, cancellationToken);
                    break;

                case QidoResponseFormat.Xml:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await DicomMetadataSerializer.WriteXmlMultipartAsync(context, datasets, cancellationToken);
                    break;

                case QidoResponseFormat.NotAcceptable:
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        // ── Frame retrieval ───────────────────────────────────────────────────

        /// <summary>
        /// Parses the HTTP <c>Accept</c> header to determine the requested media type
        /// for a WADO-RS frame retrieval request (PS3.18 Section 10.4.1.1.4).
        /// <list type="bullet">
        ///   <item>Missing / empty / <c>*/*</c> → <c>application/octet-stream</c> (PS3.18 default)</item>
        ///   <item><c>multipart/related; type="application/octet-stream"</c> → uncompressed raw pixels</item>
        ///   <item><c>multipart/related; type="image/jpeg"</c> → JPEG-compressed frames</item>
        ///   <item><c>multipart/related; type="image/jp2"</c> → JPEG 2000 frames</item>
        ///   <item><c>multipart/related; type="image/x-jls"</c> → JPEG-LS frames</item>
        ///   <item><c>multipart/related; type="image/jphc"</c> → HTJ2K frames</item>
        ///   <item><c>multipart/related; type="image/dicom-rle"</c> → RLE frames</item>
        ///   <item><c>multipart/related; type="video/mpeg2"</c> → MPEG-2 video</item>
        ///   <item><c>multipart/related; type="video/mp4"</c> → MPEG-4 / H.264 / H.265 video</item>
        ///   <item>Any other media type → 406 Not Acceptable</item>
        /// </list>
        /// </summary>
        internal static WadoFrameNegotiationResult NegotiateFrameFormat(HttpContext context)
        {
            var acceptHeader = context.Request.Headers["Accept"].ToString();

            // Missing / empty or wildcard → PS3.18 default (uncompressed octet-stream)
            if (string.IsNullOrWhiteSpace(acceptHeader) || acceptHeader.Contains("*/*"))
            {
                return WadoFrameNegotiationResult.Default;
            }

            // Extract the type= parameter value from a multipart/related Accept header.
            // E.g. from: multipart/related; type="image/jpeg"
            // extract:   image/jpeg
            const string typeParam = "type=";
            int typeIdx = acceptHeader.IndexOf(typeParam, StringComparison.OrdinalIgnoreCase);
            if (typeIdx >= 0)
            {
                int valueStart = typeIdx + typeParam.Length;
                bool quoted = valueStart < acceptHeader.Length && acceptHeader[valueStart] == '"';
                if (quoted) valueStart++;

                int valueEnd = valueStart;
                while (valueEnd < acceptHeader.Length)
                {
                    char c = acceptHeader[valueEnd];
                    if (quoted ? c == '"' : (c == ';' || c == ',' || c == ' ' || c == '\t'))
                    {
                        break;
                    }
                    valueEnd++;
                }

                var mediaType = acceptHeader.Substring(valueStart, valueEnd - valueStart).Trim().ToLowerInvariant();
                if (DicomMediaTypeMap.IsKnownFrameMimeType(mediaType))
                {
                    return new WadoFrameNegotiationResult(true, mediaType);
                }

                return WadoFrameNegotiationResult.NotAcceptable;
            }

            // No type= parameter found — check if the whole Accept value is a bare known MIME type
            var bare = acceptHeader.Trim().ToLowerInvariant();
            if (DicomMediaTypeMap.IsKnownFrameMimeType(bare))
            {
                return new WadoFrameNegotiationResult(true, bare);
            }

            return WadoFrameNegotiationResult.NotAcceptable;
        }

        /// <summary>
        /// Writes the HTTP response for a WADO-RS frame retrieval request.
        /// Success responses are written as
        /// <c>multipart/related; type="&lt;mediaType&gt;"</c> with one part per requested frame
        /// (PS3.18 Section 10.4.1.1.4).
        /// </summary>
        internal async Task WriteFramesAsync(
            HttpContext context,
            IDicomWadoFrameResponse response,
            WadoFrameNegotiationResult negotiation,
            CancellationToken cancellationToken)
        {
            if (response is DicomWadoFramesResponse framesResponse)
            {
                var mediaType = negotiation.MediaType ?? DicomMediaTypeMap.OctetStream;
                var boundary = $"----dicom-boundary-{Guid.NewGuid():N}";
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType =
                    $"multipart/related; type=\"{mediaType}\"; boundary={boundary}";

                foreach (var frame in framesResponse.Results)
                {
                    await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                    await context.Response.WriteAsync(
                        $"Content-Type: {frame.MediaType}\r\n", cancellationToken);
                    await context.Response.WriteAsync("\r\n", cancellationToken);
                    await frame.Data.CopyToStreamAsync(context.Response.Body, cancellationToken);
                    await context.Response.WriteAsync("\r\n", cancellationToken);
                }

                await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
                return;
            }

            if (response is DicomWebFailureResponse failureResponse)
            {
                await DicomWebFailureWriter.WriteAsync(context, failureResponse, cancellationToken);
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(response),
                $"Unrecognised WADO frame response type: {response?.GetType().Name}");
        }

        // ── Failure response mapping ──────────────────────────────────────────

        private static Task WriteFailureAsync(
            HttpContext context,
            IDicomWadoResponse response,
            CancellationToken cancellationToken)
        {
            if (response is DicomWebFailureResponse failureResponse)
            {
                return DicomWebFailureWriter.WriteAsync(context, failureResponse, cancellationToken);
            }

            throw new ArgumentOutOfRangeException(nameof(response),
                $"Unrecognised WADO response type: {response?.GetType().Name}");
        }

        // ── Async enumerable helpers ──────────────────────────────────────────

#pragma warning disable CS1998 // async method with no await — acceptable for yield-based adapters
        private static async IAsyncEnumerable<DicomFile> EnumerateFilesAsync(IList<DicomFile> files)
        {
            foreach (var f in files)
            {
                yield return f;
            }
        }

        private static async IAsyncEnumerable<DicomWadoRawInstance> EnumerateRawAsync(IList<DicomWadoRawInstance> parts)
        {
            foreach (var p in parts)
            {
                yield return p;
            }
        }
#pragma warning restore CS1998
    }

    // ── Frame content negotiation result ─────────────────────────────────────

    /// <summary>
    /// The result of <c>Accept</c> header negotiation for a WADO-RS frame retrieval request.
    /// </summary>
    internal readonly struct WadoFrameNegotiationResult
    {
        /// <summary>
        /// <c>false</c> when none of the client's acceptable media types are supported;
        /// the service should return HTTP 406 Not Acceptable.
        /// </summary>
        internal bool IsAcceptable { get; }

        /// <summary>
        /// The negotiated MIME type for the frame parts (e.g. <c>"application/octet-stream"</c>,
        /// <c>"image/jpeg"</c>), or <c>null</c> when <see cref="IsAcceptable"/> is <c>false</c>.
        /// </summary>
        internal string? MediaType { get; }

        internal WadoFrameNegotiationResult(bool isAcceptable, string? mediaType)
        {
            IsAcceptable = isAcceptable;
            MediaType = mediaType;
        }

        /// <summary>A pre-built result for HTTP 406 Not Acceptable.</summary>
        internal static WadoFrameNegotiationResult NotAcceptable
            => new WadoFrameNegotiationResult(false, null);

        /// <summary>A pre-built result for the default frame format (uncompressed octet-stream).</summary>
        internal static WadoFrameNegotiationResult Default
            => new WadoFrameNegotiationResult(true, DicomMediaTypeMap.OctetStream);
    }
}
