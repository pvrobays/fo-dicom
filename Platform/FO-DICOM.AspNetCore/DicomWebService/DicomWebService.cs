// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    public abstract class DicomWebService : IDicomWebService
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes the service with an optional <see cref="ILoggerFactory"/>.
        /// When <paramref name="loggerFactory"/> is <c>null</c> (e.g. in unit tests that use a
        /// no-arg constructor on a concrete subclass) a <see cref="NullLoggerFactory"/> is used
        /// so that no logging occurs but no <see cref="NullReferenceException"/> is thrown.
        /// </summary>
        protected DicomWebService(ILoggerFactory loggerFactory = null)
        {
            _logger = (loggerFactory ?? NullLoggerFactory.Instance)
                .CreateLogger(GetType().FullName);
        }

        /// <summary>
        /// Whether the DICOM JSON response should use DICOM keywords (e.g. <c>"PatientName"</c>)
        /// as JSON property names instead of the standard eight-character uppercase hexadecimal
        /// tag representation (e.g. <c>"00100010"</c>).
        /// <para>
        /// The DICOM standard (PS3.18 Section F.2.2) mandates hex tag keys.
        /// Override this property and return <c>true</c> only for non-standard/debug scenarios.
        /// Defaults to <c>false</c> (standard-compliant hex keys).
        /// </para>
        /// </summary>
        protected virtual bool WriteTagsAsKeywords => false;

        /// <summary>
        /// Whether the DICOM JSON response body should be pretty-printed with indentation.
        /// Defaults to <c>false</c> (compact JSON, suitable for API responses).
        /// </summary>
        protected virtual bool FormatJsonIndented => false;

        /// <summary>
        /// Whether an unrecognized QIDO-RS query parameter or <c>includefield</c> value should
        /// cause the entire request to fail with HTTP 400 Bad Request.
        /// <para>
        /// When <c>true</c> (default), any query string key or includefield value that cannot be
        /// resolved to a known DICOM tag throws an exception, which is surfaced to the client as
        /// a 400 response. This is the strictest standards-compliant behaviour.
        /// </para>
        /// <para>
        /// When <c>false</c>, unrecognized parameters are silently skipped and the request
        /// continues with the remaining valid parameters. Override and return <c>false</c> to
        /// allow lenient clients or vendor-specific extensions without breaking queries.
        /// </para>
        /// </summary>
        protected virtual bool StrictQueryParameterParsing => true;

        /// <summary>
        /// The warn-agent identifier included in HTTP <c>Warning</c> response headers
        /// (RFC 7234 Section 5.5, PS3.18 Section 8.3.4).
        /// <para>
        /// When <c>null</c> (default), the value of the <c>Host</c> request header is used
        /// (e.g. <c>"pacs.example.com:8080"</c>).  Override to supply a fixed service name
        /// (e.g. <c>"my-pacs.example.com"</c>) that is independent of the Host header.
        /// </para>
        /// </summary>
        protected virtual string ServiceName => null;

        public async Task HandleQidoStudiesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Study, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, request, cancellationToken);
        }

        public async Task HandleQidoSeriesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Series, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, request, cancellationToken);
        }

        public async Task HandleQidoInstancesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Image, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, request, cancellationToken);
        }

        private async Task ExecuteQidoResponseOnHttpContext(HttpContext context, IDicomQidoResponse response,
            DicomQidoRequest request, CancellationToken cancellationToken)
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

                case DicomQidoBadRequestResponse badRequestResponse:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    if (badRequestResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(badRequestResponse.Reason,
                            cancellationToken: cancellationToken);
                    }
                    break;

                case DicomQidoForbiddenResponse forbiddenResponse:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    break;

                case DicomQidoRequestTooBroadResponse requestTooBroadResponse:
                    context.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
                    break;

                case DicomQidoUnauthorizedResponse unauthorizedResponse:
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    break;

                case DicomQidoUnavailableResponse unavailableResponse:
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    if (unavailableResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(unavailableResponse.Reason,
                            cancellationToken: cancellationToken);
                    }
                    break;

                case DicomQidoNotImplementedResponse notImplementedResponse:
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(response));
            }
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
            DicomQidoRequest request)
        {
            var serviceAgent = ServiceName ?? context.Request.Host.ToString();

            // PS3.18 Section 8.3.4 / RFC 7234 §5.5: fuzzy matching not supported
            if (request != null && request.Options.IsFuzzyMatching && !successResponse.IsFuzzyMatchingSupported)
            {
                context.Response.Headers.Append("Warning",
                    $"299 {serviceAgent} \"The fuzzymatching parameter is not supported. Only literal matching has been performed.\"");
            }

            // PS3.18 Section 8.3.4.4: server maximum results exceeded
            if (successResponse.IsServerMaximumResultsReached)
            {
                context.Response.Headers.Append("Warning",
                    $"299 {serviceAgent} \"The number of results exceeded the maximum supported by the server. Additional results can be requested.\"");
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

        private async Task WriteJsonResponseAsync(HttpContext context, IList<DicomDataset> results,
            CancellationToken cancellationToken)
        {
            context.Response.ContentType = "application/dicom+json";
            await context.Response.WriteAsync(
                DicomJson.ConvertDicomToJson(results, WriteTagsAsKeywords, FormatJsonIndented),
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

        /// <summary>
        /// Parses the incoming request into a <see cref="DicomQidoRequest"/>, injects route-scoped
        /// UIDs, calls the provider, and returns both the provider's response and the parsed request
        /// (so that the caller can inspect request options such as
        /// <see cref="DicomQidoRequestOptions.IsFuzzyMatching"/> when building Warning headers).
        /// <para>
        /// The returned <see cref="DicomQidoRequest"/> is <c>null</c> when request parsing fails
        /// (the response will be a <see cref="DicomQidoBadRequestResponse"/> in that case).
        /// </para>
        /// </summary>
        private async Task<(IDicomQidoResponse response, DicomQidoRequest request)> InnerHandleQidoRequestAsync(
            DicomQueryRetrieveLevel level, HttpContext context, CancellationToken cancellationToken)
        {
            if (!(this is IDicomQidoProvider thisAsQidoProvider))
            {
                _logger.LogDebug("QIDO {Level} request received but no IDicomQidoProvider is implemented — returning 501", level);
                return (new DicomQidoNotImplementedResponse(), null);
            }

            DicomQidoRequest request;
            try
            {
                request = QueryToDicomDatasetMapper.Map(level, context.Request.Query, StrictQueryParameterParsing);

                // Inject route-scoped UIDs as match constraints.
                // These come from URL path templates (e.g. /studies/{studyInstanceUID}/series)
                // and take precedence over any query-string values for the same tag.
                if (context.Request.RouteValues.TryGetValue("studyInstanceUID", out var studyUid)
                    && studyUid is string studyUidString)
                {
                    request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUidString);
                }
                if (context.Request.RouteValues.TryGetValue("seriesInstanceUID", out var seriesUid)
                    && seriesUid is string seriesUidString)
                {
                    request.Dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, seriesUidString);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "QIDO {Level} request rejected: failed to parse query string — {Reason}",
                    level, e.Message);
                return (new DicomQidoBadRequestResponse(e.Message), null);
            }

            try
            {
                var providerResponse = await thisAsQidoProvider.OnQidoRequestAsync(request, context, cancellationToken);
                return (providerResponse, request);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "QIDO {Level} request failed: unhandled exception in OnQidoRequestAsync",
                    level);
                return (new DicomQidoUnavailableResponse(e.Message), request);
            }
        }
    }
}
