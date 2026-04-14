// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Parses the <c>multipart/related; type="application/dicom"</c> request body of a
    /// STOW-RS Store Transaction (PS3.18 Section 10.5) into <see cref="DicomFile"/> objects.
    /// <para>
    /// Uses <see cref="MultipartReader"/> from <c>Microsoft.AspNetCore.WebUtilities</c>
    /// (part of the ASP.NET Core shared framework — no additional NuGet package required).
    /// </para>
    /// </summary>
    internal static class StowRequestReader
    {
        /// <summary>
        /// Extracts the multipart boundary from the request <c>Content-Type</c> header.
        /// Returns <c>null</c> when the Content-Type is missing, not multipart/related,
        /// or has no boundary parameter.
        /// </summary>
        internal static string? GetBoundary(HttpRequest request)
        {
            var contentType = request.ContentType;
            if (string.IsNullOrEmpty(contentType))
                return null;

            if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed))
                return null;

            // Must be multipart/related
            if (!string.Equals(parsed.MediaType.Value, "multipart/related", StringComparison.OrdinalIgnoreCase))
                return null;

            var boundary = parsed.Boundary;
            if (boundary.HasValue && !Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty(boundary))
                return boundary.Value;

            return null;
        }

        /// <summary>
        /// Reads all DICOM instances from the multipart body synchronously (buffered).
        /// <para>
        /// If any part cannot be parsed as a valid <see cref="DicomFile"/> (e.g. corrupt data),
        /// returns a <see cref="StowParseError"/> with a 400-level reason instead of partial results.
        /// </para>
        /// </summary>
        /// <param name="request">The incoming HTTP request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="StowReadResult"/> containing either the parsed files or an error.
        /// </returns>
        internal static async Task<StowReadResult> ReadAllAsync(
            HttpRequest request,
            CancellationToken cancellationToken)
        {
            var boundary = GetBoundary(request);
            if (boundary == null)
                return StowReadResult.Failure("Request Content-Type must be multipart/related with a boundary parameter.");

            var reader = new MultipartReader(boundary, request.Body);
            var files = new List<DicomFile>();
            int partIndex = 0;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                partIndex++;

                // Validate part Content-Type
                var partContentType = section.ContentType;
                if (!IsApplicationDicom(partContentType))
                    return StowReadResult.Failure(
                        $"Part {partIndex} has unsupported Content-Type '{partContentType}'. " +
                        "Only 'application/dicom' parts are supported.");

                // Buffer the part body so we can parse it (MultipartReader streams are not seekable)
                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                ms.Position = 0;

                DicomFile dicomFile;
                try
                {
                    dicomFile = await DicomFile.OpenAsync(ms, FileReadOption.ReadAll).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return StowReadResult.Failure(
                        $"Part {partIndex} could not be parsed as a DICOM file: {ex.Message}");
                }

                files.Add(dicomFile);
            }

            if (files.Count == 0)
                return StowReadResult.Failure("Request body contained no DICOM parts.");

            return StowReadResult.Success(files);
        }

        /// <summary>
        /// Streams DICOM instances from the multipart body one at a time.
        /// <para>
        /// Yields each successfully parsed <see cref="DicomFile"/> as it is read. If a part
        /// cannot be parsed, the async enumerable terminates and the caller receives a
        /// <see cref="StowStreamError"/> exception.
        /// </para>
        /// </summary>
        /// <param name="request">The incoming HTTP request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Async sequence of parsed <see cref="DicomFile"/> objects.</returns>
        /// <exception cref="StowStreamError">
        /// Thrown when a part cannot be parsed (invalid Content-Type or corrupt DICOM data).
        /// </exception>
        internal static async IAsyncEnumerable<DicomFile> StreamAsync(
            HttpRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var boundary = GetBoundary(request);
            if (boundary == null)
                throw new StowStreamError("Request Content-Type must be multipart/related with a boundary parameter.");

            var reader = new MultipartReader(boundary, request.Body);
            int partIndex = 0;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                partIndex++;

                var partContentType = section.ContentType;
                if (!IsApplicationDicom(partContentType))
                    throw new StowStreamError(
                        $"Part {partIndex} has unsupported Content-Type '{partContentType}'. " +
                        "Only 'application/dicom' parts are supported.");

                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                ms.Position = 0;

                DicomFile dicomFile;
                try
                {
                    dicomFile = await DicomFile.OpenAsync(ms, FileReadOption.ReadAll).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new StowStreamError(
                        $"Part {partIndex} could not be parsed as a DICOM file: {ex.Message}", ex);
                }

                yield return dicomFile;
            }

            if (partIndex == 0)
                throw new StowStreamError("Request body contained no DICOM parts.");
        }

        private static bool IsApplicationDicom(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return false;
            // Accept "application/dicom" with optional parameters (e.g. transfer-syntax)
            var semicolon = contentType.IndexOf(';');
            var mediaType = semicolon >= 0
                ? contentType.Substring(0, semicolon).Trim()
                : contentType.Trim();
            return string.Equals(mediaType, "application/dicom", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Result of a buffered STOW-RS multipart read operation.
    /// Either carries the parsed <see cref="DicomFile"/> list or a human-readable error reason.
    /// </summary>
    internal sealed class StowReadResult
    {
        /// <summary>The parsed files. Non-null when <see cref="IsSuccess"/> is <c>true</c>.</summary>
        public IReadOnlyList<DicomFile>? Files { get; }

        /// <summary>
        /// Human-readable error reason. Non-null when <see cref="IsSuccess"/> is <c>false</c>.
        /// </summary>
        public string? ErrorReason { get; }

        /// <summary><c>true</c> when parsing succeeded and <see cref="Files"/> is populated.</summary>
        public bool IsSuccess => Files != null;

        private StowReadResult(IReadOnlyList<DicomFile>? files, string? errorReason)
        {
            Files = files;
            ErrorReason = errorReason;
        }

        internal static StowReadResult Success(IReadOnlyList<DicomFile> files)
            => new StowReadResult(files, null);

        internal static StowReadResult Failure(string reason)
            => new StowReadResult(null, reason);
    }

    /// <summary>
    /// Exception thrown by <see cref="StowRequestReader.StreamAsync"/> when a multipart part
    /// cannot be parsed (invalid Content-Type, missing boundary, or corrupt DICOM data).
    /// </summary>
    internal sealed class StowStreamError : Exception
    {
        public StowStreamError(string message) : base(message) { }
        public StowStreamError(string message, Exception inner) : base(message, inner) { }
    }
}
