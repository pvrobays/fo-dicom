// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    public abstract class DicomWebService : IDicomWebService
    {
        private readonly ILogger _logger;
        private QidoResponseWriter _responseWriter;

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
        /// Defaults to <c>"fo-dicom-web"</c>. Override to supply a more specific service name
        /// (e.g. <c>"pacs.example.com"</c>) that helps clients identify the origin of the warning.
        /// </para>
        /// </summary>
        protected virtual string ServiceName => "fo-dicom-web";

        /// <summary>
        /// Returns the lazily-initialised <see cref="QidoResponseWriter"/> for this service
        /// instance. The writer is created once from the virtual configuration properties and
        /// reused across all requests.
        /// </summary>
        private QidoResponseWriter ResponseWriter =>
            _responseWriter ?? (_responseWriter = new QidoResponseWriter(ServiceName, WriteTagsAsKeywords, FormatJsonIndented));

        public async Task HandleQidoStudiesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Study, context, cancellationToken);
            await ResponseWriter.ExecuteAsync(context, response, request, cancellationToken);
        }

        public async Task HandleQidoSeriesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Series, context, cancellationToken);
            await ResponseWriter.ExecuteAsync(context, response, request, cancellationToken);
        }

        public async Task HandleQidoInstancesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Image, context, cancellationToken);
            await ResponseWriter.ExecuteAsync(context, response, request, cancellationToken);
        }

        /// <summary>
        /// Parses the incoming request into a <see cref="DicomQidoRequest"/>, injects route-scoped
        /// UIDs, calls the provider, and returns both the provider's response and the parsed request
        /// (so that the caller can inspect request options such as
        /// <see cref="DicomQidoRequestOptions.IsFuzzyMatching"/> when building Warning headers).
        /// <para>
        /// The returned <see cref="DicomQidoRequest"/> is <c>null</c> when request parsing fails
        /// (the response will be a <see cref="DicomWebBadRequestResponse"/> in that case).
        /// </para>
        /// </summary>
        private async Task<(IDicomQidoResponse response, DicomQidoRequest request)> InnerHandleQidoRequestAsync(
            DicomQueryRetrieveLevel level, HttpContext context, CancellationToken cancellationToken)
        {
            if (!(this is IDicomQidoProvider thisAsQidoProvider))
            {
                _logger.LogDebug("QIDO {Level} request received but no IDicomQidoProvider is implemented — returning 501", level);
                return (new DicomWebNotImplementedResponse(), null);
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
                return (new DicomWebBadRequestResponse(e.Message), null);
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
                return (new DicomWebUnavailableResponse(e.Message), request);
            }
        }
    }
}
