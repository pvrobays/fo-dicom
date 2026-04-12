// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.DicomWeb;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
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

        internal WadoResponseWriter(string serviceAgent, bool writeTagsAsKeywords, bool formatJsonIndented)
        {
            _serviceAgent = serviceAgent;
            _writeTagsAsKeywords = writeTagsAsKeywords;
            _formatJsonIndented = formatJsonIndented;
        }

        // ── Instance retrieval ────────────────────────────────────────────────

        /// <summary>
        /// Writes the HTTP response for a WADO-RS instance retrieval request.
        /// Success responses are written as <c>multipart/related; type="application/dicom"</c>
        /// (PS3.18 Table 10.4.4-1). Failure responses are mapped to the appropriate HTTP status.
        /// </summary>
        internal async Task ExecuteInstancesAsync(
            HttpContext context,
            IDicomWadoResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWadoInstancesResponse instancesResponse:
                    await WriteMultipartDicomResponseAsync(context,
                        EnumerateFilesAsync(instancesResponse.Results), cancellationToken);
                    break;

                case DicomWadoRawInstancesResponse rawResponse:
                    await WriteMultipartRawResponseAsync(context,
                        EnumerateRawAsync(rawResponse.Results), cancellationToken);
                    break;

                case DicomWadoAsyncInstancesResponse asyncResponse:
                    await WriteMultipartDicomResponseAsync(context, asyncResponse.Results, cancellationToken);
                    break;

                case DicomWadoAsyncRawInstancesResponse asyncRawResponse:
                    await WriteMultipartRawResponseAsync(context, asyncRawResponse.Results, cancellationToken);
                    break;

                default:
                    await WriteFailureResponseAsync(context, response, cancellationToken);
                    break;
            }
        }

        // ── Metadata retrieval ────────────────────────────────────────────────

        /// <summary>
        /// Writes the HTTP response for a WADO-RS metadata retrieval request.
        /// Success responses use the same content negotiation as QIDO-RS metadata:
        /// <c>application/dicom+json</c> (default) or
        /// <c>multipart/related; type="application/dicom+xml"</c> (PS3.18 Table 10.4.4-1).
        /// </summary>
        internal async Task ExecuteMetadataAsync(
            HttpContext context,
            IDicomWadoResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWadoMetadataResponse metadataResponse:
                    await WriteMetadataAsync(context, metadataResponse.Results, cancellationToken);
                    break;

                case DicomWadoAsyncMetadataResponse asyncMetadataResponse:
                    var datasets = new List<DicomDataset>();
                    await foreach (var ds in asyncMetadataResponse.Results.WithCancellation(cancellationToken))
                    {
                        datasets.Add(ds);
                    }
                    await WriteMetadataAsync(context, datasets, cancellationToken);
                    break;

                default:
                    await WriteFailureResponseAsync(context, response, cancellationToken);
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

        private static async Task WriteMultipartDicomResponseAsync(
            HttpContext context,
            IAsyncEnumerable<DicomFile> files,
            CancellationToken cancellationToken)
        {
            var boundary = $"----dicom-boundary-{Guid.NewGuid():N}";
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom\"; boundary={boundary}";

            await foreach (var file in files.WithCancellation(cancellationToken))
            {
                // Part header
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: application/dicom\r\n\r\n", cancellationToken);

                // Part body: serialize the DicomFile into a temporary buffer then flush it
                using (var ms = new MemoryStream())
                {
                    await file.SaveAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(context.Response.Body, 81920, cancellationToken);
                }
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            // Closing boundary
            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }

        private static async Task WriteMultipartRawResponseAsync(
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
                // Part header — include transfer-syntax parameter when known
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                if (part.TransferSyntaxUid != null)
                {
                    await context.Response.WriteAsync(
                        $"Content-Type: application/dicom; transfer-syntax={part.TransferSyntaxUid}\r\n\r\n",
                        cancellationToken);
                }
                else
                {
                    await context.Response.WriteAsync("Content-Type: application/dicom\r\n\r\n", cancellationToken);
                }

                await part.Data.CopyToAsync(context.Response.Body, 81920, cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }

        // ── Metadata writing ──────────────────────────────────────────────────

        private async Task WriteMetadataAsync(
            HttpContext context,
            IList<DicomDataset> datasets,
            CancellationToken cancellationToken)
        {
            var format = NegotiateMetadataFormat(context);
            switch (format)
            {
                case QidoResponseFormat.Json:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/dicom+json";
                    await context.Response.WriteAsync(
                        DicomJson.ConvertDicomToJson(datasets, _writeTagsAsKeywords, _formatJsonIndented),
                        cancellationToken: cancellationToken);
                    break;

                case QidoResponseFormat.Xml:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await WriteXmlMultipartMetadataAsync(context, datasets, cancellationToken);
                    break;

                case QidoResponseFormat.NotAcceptable:
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static async Task WriteXmlMultipartMetadataAsync(
            HttpContext context,
            IList<DicomDataset> datasets,
            CancellationToken cancellationToken)
        {
            var boundary = Guid.NewGuid().ToString("N");
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom+xml\"; boundary={boundary}";

            var sb = new StringBuilder();

            if (datasets.Count == 0)
            {
                sb.Append("--").AppendLine(boundary);
                sb.AppendLine("Content-Type: application/dicom+xml");
                sb.AppendLine();
                sb.AppendLine(DicomXML.ConvertDicomToXML(new DicomDataset()));
            }
            else
            {
                foreach (var dataset in datasets)
                {
                    sb.Append("--").AppendLine(boundary);
                    sb.AppendLine("Content-Type: application/dicom+xml");
                    sb.AppendLine();
                    sb.AppendLine(DicomXML.ConvertDicomToXML(dataset));
                }
            }

            sb.Append("--").Append(boundary).AppendLine("--");
            await context.Response.WriteAsync(sb.ToString(), cancellationToken: cancellationToken);
        }

        // ── Failure response mapping ──────────────────────────────────────────

        private static async Task WriteFailureResponseAsync(
            HttpContext context,
            IDicomWadoResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWebBadRequestResponse badRequestResponse:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    if (badRequestResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(badRequestResponse.Reason, cancellationToken);
                    }
                    break;

                case DicomWebUnauthorizedResponse _:
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    break;

                case DicomWebForbiddenResponse _:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    break;

                case DicomWebNotFoundResponse notFoundResponse:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    if (notFoundResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(notFoundResponse.Reason, cancellationToken);
                    }
                    break;

                case DicomWebNotImplementedResponse _:
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    break;

                case DicomWebUnavailableResponse unavailableResponse:
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    if (unavailableResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(unavailableResponse.Reason, cancellationToken);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(response),
                        $"Unrecognised WADO response type: {response?.GetType().Name}");
            }
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
}
