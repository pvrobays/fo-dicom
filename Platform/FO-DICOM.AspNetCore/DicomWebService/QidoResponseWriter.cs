// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.DicomWeb;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// The response format negotiated from the HTTP <c>Accept</c> header.
    /// </summary>
    internal enum QidoResponseFormat
    {
        /// <summary>Single-part <c>application/dicom+json</c> body.</summary>
        Json,

        /// <summary>Multipart <c>multipart/related; type="application/dicom+xml"</c> body.</summary>
        Xml,

        /// <summary>The requested media type is not supported — respond with 406.</summary>
        NotAcceptable
    }

    /// <summary>
    /// Translates a <see cref="IDicomQidoResponse"/> into an HTTP response, handling content
    /// negotiation, response serialization (JSON and multipart XML), and PS3.18 Warning headers.
    /// <para>
    /// Instances are reusable across requests — create one per <see cref="DicomWebService"/>
    /// configuration and retain it for the lifetime of the service.
    /// </para>
    /// </summary>
    internal class QidoResponseWriter
    {
        private readonly string _serviceAgent;
        private readonly bool _writeTagsAsKeywords;
        private readonly bool _formatJsonIndented;

        /// <summary>
        /// Creates a writer with the given formatting configuration.
        /// </summary>
        /// <param name="serviceAgent">
        /// The warn-agent identifier for RFC 7234 <c>Warning</c> headers
        /// (e.g. <c>"fo-dicom-web"</c> or <c>"pacs.example.com"</c>).
        /// </param>
        /// <param name="writeTagsAsKeywords">
        /// When <c>true</c>, DICOM JSON uses keyword names instead of hex tag keys.
        /// </param>
        /// <param name="formatJsonIndented">
        /// When <c>true</c>, JSON output is pretty-printed.
        /// </param>
        internal QidoResponseWriter(string serviceAgent, bool writeTagsAsKeywords, bool formatJsonIndented)
        {
            _serviceAgent = serviceAgent;
            _writeTagsAsKeywords = writeTagsAsKeywords;
            _formatJsonIndented = formatJsonIndented;
        }

        /// <summary>
        /// Writes the full HTTP response for a QIDO-RS result, including status code, headers,
        /// and body serialization.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="response">The QIDO response returned by the provider.</param>
        /// <param name="request">
        /// The parsed QIDO request, or <c>null</c> when request parsing failed
        /// (in which case <paramref name="response"/> is a <see cref="DicomWebBadRequestResponse"/>).
        /// </param>
        /// <param name="cancellationToken">Propagated cancellation token.</param>
        internal async Task ExecuteAsync(
            HttpContext context,
            IDicomQidoResponse response,
            DicomQidoRequest? request,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomQidoSuccessResponse successResponse:
                    // Emit Warning headers before writing the body (headers must be set first).
                    EmitWarningHeaders(context, successResponse, request);

                    var format = NegotiateResponseFormat(context);
                    switch (format)
                    {
                        case QidoResponseFormat.Json:
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await WriteJsonResponseAsync(context, successResponse.Results, cancellationToken);
                            break;

                        case QidoResponseFormat.Xml:
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await WriteXmlMultipartResponseAsync(context, successResponse.Results, cancellationToken);
                            break;

                        case QidoResponseFormat.NotAcceptable:
                            context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(nameof(format));
                    }
                    break;

                case DicomWebBadRequestResponse badRequestResponse:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    if (badRequestResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(badRequestResponse.Reason,
                            cancellationToken: cancellationToken);
                    }
                    break;

                case DicomQidoRequestTooBroadResponse _:
                    context.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
                    break;

                case DicomWebForbiddenResponse _:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    break;

                case DicomWebUnauthorizedResponse _:
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    break;

                case DicomWebNotFoundResponse notFoundResponse:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    if (notFoundResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(notFoundResponse.Reason,
                            cancellationToken: cancellationToken);
                    }
                    break;

                case DicomWebUnavailableResponse unavailableResponse:
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    if (unavailableResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(unavailableResponse.Reason,
                            cancellationToken: cancellationToken);
                    }
                    break;

                case DicomWebNotImplementedResponse _:
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(response));
            }
        }

        /// <summary>
        /// Determines the response format from the HTTP <c>Accept</c> header.
        /// <list type="bullet">
        ///   <item><c>application/dicom+json</c> or <c>application/json</c> (backward compat) → JSON</item>
        ///   <item><c>multipart/related; type="application/dicom+xml"</c> or <c>application/dicom+xml</c> (relaxed) → XML</item>
        ///   <item><c>*/*</c> or missing header → JSON (pragmatic default)</item>
        ///   <item>Anything else → <see cref="QidoResponseFormat.NotAcceptable"/></item>
        /// </list>
        /// </summary>
        internal static QidoResponseFormat NegotiateResponseFormat(HttpContext context)
        {
            var acceptHeader = context.Request.Headers["Accept"].ToString();

            // Missing or empty Accept header → default to JSON (pragmatic, non-strict)
            if (string.IsNullOrWhiteSpace(acceptHeader))
            {
                return QidoResponseFormat.Json;
            }

            // Wildcard → JSON
            if (acceptHeader.Contains("*/*"))
            {
                return QidoResponseFormat.Json;
            }

            // Check for JSON variants (standard DICOM JSON and legacy application/json)
            if (acceptHeader.IndexOf("application/dicom+json", StringComparison.OrdinalIgnoreCase) >= 0
                || acceptHeader.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return QidoResponseFormat.Json;
            }

            // Check for XML variants:
            // Standard: multipart/related; type="application/dicom+xml"
            // Relaxed:  application/dicom+xml (bare, without multipart wrapper in the Accept header)
            if (acceptHeader.IndexOf("application/dicom+xml", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return QidoResponseFormat.Xml;
            }

            return QidoResponseFormat.NotAcceptable;
        }

        /// <summary>
        /// Appends PS3.18 Section 8.3.4 <c>Warning: 299</c> headers to the response when applicable.
        /// <list type="bullet">
        ///   <item>Fuzzy-matching warning — emitted when the client requested fuzzy matching
        ///     (<see cref="DicomQidoRequestOptions.IsFuzzyMatching"/>) but the provider does not
        ///     support it (<see cref="DicomQidoSuccessResponse.IsFuzzyMatchingSupported"/> is
        ///     <c>false</c>).</item>
        ///   <item>Maximum-results warning — emitted when the provider indicates that additional
        ///     matching results exist beyond what was returned
        ///     (<see cref="DicomQidoSuccessResponse.IsServerMaximumResultsReached"/>).</item>
        /// </list>
        /// </summary>
        private void EmitWarningHeaders(HttpContext context, DicomQidoSuccessResponse successResponse,
            DicomQidoRequest? request)
        {
            // PS3.18 Section 8.3.4 / RFC 7234 §5.5: fuzzy matching not supported
            if (request != null && request.Options.IsFuzzyMatching && !successResponse.IsFuzzyMatchingSupported)
            {
                context.Response.Headers.Append("Warning",
                    $"299 {_serviceAgent} \"The fuzzymatching parameter is not supported. Only literal matching has been performed.\"");
            }

            // PS3.18 Section 8.3.4.4: server maximum results exceeded
            if (successResponse.IsServerMaximumResultsReached)
            {
                context.Response.Headers.Append("Warning",
                    $"299 {_serviceAgent} \"The number of results exceeded the maximum supported by the server. Additional results can be requested.\"");
            }
        }

        private async Task WriteJsonResponseAsync(HttpContext context, IList<DicomDataset> results,
            CancellationToken cancellationToken)
        {
            context.Response.ContentType = "application/dicom+json";
            await context.Response.WriteAsync(
                DicomJson.ConvertDicomToJson(results, _writeTagsAsKeywords, _formatJsonIndented),
                cancellationToken: cancellationToken);
        }

        private static async Task WriteXmlMultipartResponseAsync(HttpContext context, IList<DicomDataset> results,
            CancellationToken cancellationToken)
        {
            var boundary = Guid.NewGuid().ToString("N");
            context.Response.ContentType = $"multipart/related; type=\"application/dicom+xml\"; boundary={boundary}";

            var sb = new StringBuilder();

            if (results.Count == 0)
            {
                // PS3.18 Section 8.3.4.4.1: empty result is encoded as a single part
                // with an empty NativeDicomModel element.
                sb.Append("--").AppendLine(boundary);
                sb.AppendLine("Content-Type: application/dicom+xml");
                sb.AppendLine();
                sb.AppendLine(DicomXML.ConvertDicomToXML(new DicomDataset()));
            }
            else
            {
                foreach (var dataset in results)
                {
                    sb.Append("--").AppendLine(boundary);
                    sb.AppendLine("Content-Type: application/dicom+xml");
                    sb.AppendLine();
                    sb.AppendLine(DicomXML.ConvertDicomToXML(dataset));
                }
            }

            // Closing boundary
            sb.Append("--").Append(boundary).AppendLine("--");

            await context.Response.WriteAsync(sb.ToString(), cancellationToken: cancellationToken);
        }
    }
}
