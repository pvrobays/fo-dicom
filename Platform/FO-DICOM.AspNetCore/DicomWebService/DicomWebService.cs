// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.DicomWeb;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    public abstract class DicomWebService : IDicomWebService
    {
        private readonly ILogger _logger;
        private QidoResponseWriter? _responseWriter;
        private WadoResponseWriter? _wadoResponseWriter;

        /// <summary>
        /// Initializes the service with an optional <see cref="ILoggerFactory"/>.
        /// When <paramref name="loggerFactory"/> is <c>null</c> (e.g. in unit tests that use a
        /// no-arg constructor on a concrete subclass) a <see cref="NullLoggerFactory"/> is used
        /// so that no logging occurs but no <see cref="NullReferenceException"/> is thrown.
        /// </summary>
        protected DicomWebService(ILoggerFactory? loggerFactory = null)
        {
            _logger = (loggerFactory ?? NullLoggerFactory.Instance)
                .CreateLogger(GetType().FullName ?? GetType().Name);
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
        /// Controls how <c>Content-Location</c> headers are formatted in WADO-RS multipart
        /// responses (PS3.18 Section 10.4.1.1).
        /// <para>
        /// <see cref="ContentLocationMode.Relative"/> (default) emits path-absolute values
        /// (e.g. <c>/dicomweb/studies/…/instances/…</c>) which work correctly behind reverse
        /// proxies without additional configuration.
        /// </para>
        /// <para>
        /// <see cref="ContentLocationMode.Absolute"/> prepends the request scheme and host
        /// (e.g. <c>https://pacs.example.com/dicomweb/…</c>); configure
        /// <c>ForwardedHeaders</c> middleware when behind a reverse proxy so the correct
        /// external scheme and host are used.
        /// </para>
        /// <para>
        /// <see cref="ContentLocationMode.None"/> suppresses the header entirely.
        /// </para>
        /// </summary>
        protected virtual ContentLocationMode ContentLocationMode => ContentLocationMode.Relative;

        /// <summary>
        /// The minimum number of bytes below which a bulk data element is kept inline as
        /// <c>"InlineBinary"</c> in metadata responses (WADO-RS JSON / XML). Elements with
        /// <see cref="FellowOakDicom.IO.Buffer.IByteBuffer.Size"/> &gt;
        /// <see cref="BulkDataInlineThreshold"/> are replaced with a <c>"BulkDataURI"</c>
        /// reference pointing to the bulk data retrieval endpoint.
        /// <para>
        /// The default is <c>0</c>, meaning all bulk data elements (OB, OD, OF, OL, OV, OW, UN)
        /// are replaced with URIs, which is the behaviour mandated by PS3.18 Section 10.4.1.1.2.
        /// </para>
        /// <para>
        /// Override and return a larger value to keep small elements (e.g. icon pixel data)
        /// inline; only elements strictly larger than the threshold are replaced.
        /// </para>
        /// </summary>
        protected virtual long BulkDataInlineThreshold => 0;

        /// <summary>
        /// Returns the lazily-initialised <see cref="QidoResponseWriter"/> for this service
        /// instance. The writer is created once from the virtual configuration properties and
        /// reused across all requests. Uses <see cref="Interlocked.CompareExchange{T}"/> for
        /// thread-safe initialisation without locking.
        /// </summary>
        private QidoResponseWriter ResponseWriter
        {
            get
            {
                var existing = _responseWriter;
                if (existing != null) return existing;
                var created = new QidoResponseWriter(ServiceName, WriteTagsAsKeywords, FormatJsonIndented);
                return Interlocked.CompareExchange(ref _responseWriter, created, null) ?? created;
            }
        }

        /// <summary>
        /// Returns the lazily-initialised <see cref="WadoResponseWriter"/> for this service
        /// instance. The writer is created once from the virtual configuration properties and
        /// reused across all requests. Uses <see cref="Interlocked.CompareExchange{T}"/> for
        /// thread-safe initialisation without locking.
        /// </summary>
        private WadoResponseWriter WadoWriter
        {
            get
            {
                var existing = _wadoResponseWriter;
                if (existing != null) return existing;
                var created = new WadoResponseWriter(
                    ServiceName, WriteTagsAsKeywords, FormatJsonIndented,
                    ContentLocationMode, BulkDataInlineThreshold);
                return Interlocked.CompareExchange(ref _wadoResponseWriter, created, null) ?? created;
            }
        }

        public async Task HandleQidoStudiesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Study, context, cancellationToken);
            await ResponseWriter.WriteAsync(context, response, request, cancellationToken);
        }

        public async Task HandleQidoSeriesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Series, context, cancellationToken);
            await ResponseWriter.WriteAsync(context, response, request, cancellationToken);
        }

        public async Task HandleQidoInstancesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var (response, request) = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Image, context, cancellationToken);
            await ResponseWriter.WriteAsync(context, response, request, cancellationToken);
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
        private async Task<(IDicomQidoResponse response, DicomQidoRequest? request)> InnerHandleQidoRequestAsync(
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
                var studyUidString = RouteUidHelper.GetRouteUid(context, "studyInstanceUID");
                if (studyUidString != null)
                {
                    request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUidString);
                }
                var seriesUidString = RouteUidHelper.GetRouteUid(context, "seriesInstanceUID");
                if (seriesUidString != null)
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

        // ── WADO-RS ─────────────────────────────────────────────────────────────

        public async Task HandleWadoInstancesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;

            // Negotiate transfer syntax from Accept header before invoking the provider.
            // A 406 result means no supported media type was found — short-circuit immediately.
            var negotiation = WadoResponseWriter.NegotiateInstanceFormat(context);
            if (!negotiation.IsAcceptable)
            {
                context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return;
            }

            var wadoRequest = BuildWadoRequest(context, negotiation);
            var response = await InnerHandleWadoRequestAsync<IDicomWadoInstanceResponse>(
                wadoRequest, context, cancellationToken,
                (provider, req, ctx, ct) => provider.OnRetrieveInstancesAsync(req, ctx, ct));
            await WadoWriter.WriteInstancesAsync(context, response, negotiation, cancellationToken);
        }

        public async Task HandleWadoMetadataRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var wadoRequest = BuildWadoRequest(context);
            var response = await InnerHandleWadoRequestAsync<IDicomWadoMetadataResponse>(
                wadoRequest, context, cancellationToken,
                (provider, req, ctx, ct) => provider.OnRetrieveMetadataAsync(req, ctx, ct));
            await WadoWriter.WriteMetadataAsync(context, response, cancellationToken);
        }

        public async Task HandleWadoFramesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;

            // Negotiate MIME type from Accept header before invoking the provider.
            var negotiation = WadoResponseWriter.NegotiateFrameFormat(context);
            if (!negotiation.IsAcceptable)
            {
                context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return;
            }

            // Parse the {frameList} route value ("1", "1,3,5", etc.)
            int[] frameNumbers;
            try
            {
                var parsed = ParseFrameList(RouteUidHelper.GetRouteUid(context, "frameList"));
                if (parsed.Length == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                frameNumbers = parsed;
            }
            catch (FormatException ex)
            {
                _logger.LogWarning("WADO frames request rejected: invalid frameList — {Reason}", ex.Message);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Build WADO request and call the provider via OnRetrieveInstancesAsync.
            var studyUid = RouteUidHelper.GetRouteUid(context, "studyInstanceUID") ?? string.Empty;
            var seriesUid = RouteUidHelper.GetRouteUid(context, "seriesInstanceUID");
            var sopUid = RouteUidHelper.GetRouteUid(context, "sopInstanceUID");
            var wadoRequest = new DicomWadoRequest(studyUid, seriesUid, sopUid, frameNumbers);

            var instanceResponse = await InnerHandleWadoRequestAsync<IDicomWadoInstanceResponse>(
                wadoRequest, context, cancellationToken,
                (provider, req, ctx, ct) => provider.OnRetrieveInstancesAsync(req, ctx, ct));

            // If provider returned a failure, surface it directly.
            if (instanceResponse is DicomWebFailureResponse)
            {
                await WadoWriter.WriteFramesAsync(context, (IDicomWadoFrameResponse)instanceResponse, negotiation, cancellationToken);
                return;
            }

            // Extract frames from the DicomFile(s) returned by the provider.
            IDicomWadoFrameResponse? frameResponse;
            if (instanceResponse is DicomWadoInstancesResponse instancesResponse)
            {
                frameResponse = ExtractFrames(instancesResponse.Results, frameNumbers, negotiation);
                if (frameResponse == null)
                {
                    // ExtractFrames returns null when the requested MIME type is incompatible.
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    return;
                }
            }
            else
            {
                // DicomWadoRawInstancesResponse and async variants are not supported for frame extraction.
                context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return;
            }

            await WadoWriter.WriteFramesAsync(context, frameResponse, negotiation, cancellationToken);
        }

        /// <summary>
        /// Parses a comma-separated frame list string (e.g. <c>"1,3,5"</c>) into an array of
        /// 1-based frame numbers. Throws <see cref="FormatException"/> when any token is
        /// non-numeric, zero, or negative.
        /// </summary>
        private static int[] ParseFrameList(string? frameList)
        {
            if (string.IsNullOrWhiteSpace(frameList))
            {
                throw new FormatException("frameList is missing or empty");
            }

            var parts = frameList.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim();
                if (!int.TryParse(token, out int n))
                {
                    throw new FormatException($"Non-numeric frame number '{token}'");
                }
                if (n < 1)
                {
                    throw new FormatException($"Frame number must be >= 1, got {n}");
                }
                result[i] = n;
            }
            return result;
        }

        /// <summary>
        /// Extracts the requested frames from the provider's <see cref="DicomFile"/> results.
        /// Returns a <see cref="DicomWadoFramesResponse"/> on success, a
        /// <see cref="DicomWebFailureResponse"/> on a bad-request error (e.g. frame out of range),
        /// or <c>null</c> when the requested MIME type is incompatible with the instance's
        /// transfer syntax (caller should return 406).
        /// </summary>
        private static IDicomWadoFrameResponse? ExtractFrames(
            IList<DicomFile> files,
            int[] frameNumbers,
            WadoFrameNegotiationResult negotiation)
        {
            // Frame retrieval requires exactly one instance.
            if (files.Count == 0)
            {
                return new DicomWebNotFoundResponse();
            }

            var file = files[0];
            var dataset = file.Dataset;
            var pixelData = DicomPixelData.Create(dataset);
            var totalFrames = pixelData.NumberOfFrames;

            // Validate all requested frame numbers before extracting any.
            foreach (var frameNumber in frameNumbers)
            {
                if (frameNumber > totalFrames)
                {
                    return new DicomWebBadRequestResponse(
                        $"Frame {frameNumber} is out of range; instance has {totalFrames} frame(s)");
                }
            }

            var syntax = file.FileMetaInfo?.TransferSyntax ?? dataset.InternalTransferSyntax;
            var nativeMime = DicomMediaTypeMap.GetMimeType(syntax);
            var requestedMime = negotiation.MediaType ?? DicomMediaTypeMap.OctetStream;
            bool isCompressed = syntax.IsEncapsulated;

            // If the client wants a compressed format that does not match the native syntax → 406.
            // Return null to signal that the caller should send 406.
            if (requestedMime != DicomMediaTypeMap.OctetStream && requestedMime != nativeMime)
            {
                return null;
            }

            var frames = new List<DicomWadoFrameData>(frameNumbers.Length);
            foreach (var frameNumber in frameNumbers)
            {
                int zeroBasedIndex = frameNumber - 1;
                IByteBuffer frameBuffer;
                string partMime;

                if (requestedMime == DicomMediaTypeMap.OctetStream && isCompressed)
                {
                    // Decompress to raw pixels using DicomTranscoder.
                    var transcoder = new DicomTranscoder(syntax, DicomTransferSyntax.ExplicitVRLittleEndian);
                    frameBuffer = transcoder.DecodeFrame(dataset, zeroBasedIndex);
                    partMime = DicomMediaTypeMap.OctetStream;
                }
                else
                {
                    // Return native frame bytes (compressed or already uncompressed).
                    frameBuffer = pixelData.GetFrame(zeroBasedIndex);
                    partMime = nativeMime;
                }

                frames.Add(new DicomWadoFrameData(frameBuffer, partMime, frameNumber));
            }

            return new DicomWadoFramesResponse(frames);
        }

        /// <summary>
        /// Builds a <see cref="DicomWadoRequest"/> from the route values in the current HTTP context
        /// and the pre-negotiated transfer-syntax preference.
        /// </summary>
        private static DicomWadoRequest BuildWadoRequest(HttpContext context, WadoInstanceNegotiationResult negotiation)
        {
            var studyUid = RouteUidHelper.GetRouteUid(context, "studyInstanceUID") ?? string.Empty;
            var seriesUid = RouteUidHelper.GetRouteUid(context, "seriesInstanceUID");
            var sopUid = RouteUidHelper.GetRouteUid(context, "sopInstanceUID");
            return new DicomWadoRequest(
                studyUid, seriesUid, sopUid,
                negotiation.RequestedTransferSyntax,
                negotiation.AcceptsAnyTransferSyntax);
        }

        /// <summary>
        /// Builds a <see cref="DicomWadoRequest"/> from the route values in the current HTTP context,
        /// without transfer-syntax negotiation (used for metadata requests).
        /// </summary>
        private static DicomWadoRequest BuildWadoRequest(HttpContext context)
        {
            var studyUid = RouteUidHelper.GetRouteUid(context, "studyInstanceUID") ?? string.Empty;
            var seriesUid = RouteUidHelper.GetRouteUid(context, "seriesInstanceUID");
            var sopUid = RouteUidHelper.GetRouteUid(context, "sopInstanceUID");
            return new DicomWadoRequest(studyUid, seriesUid, sopUid);
        }

        /// <summary>
        /// Checks that an <see cref="IDicomWadoProvider"/> is implemented, then delegates to the
        /// appropriate provider method. Returns a failure response on missing provider or exception.
        /// <typeparamref name="TResponse"/> is either <see cref="IDicomWadoInstanceResponse"/> or
        /// <see cref="IDicomWadoMetadataResponse"/>, giving each call-site compile-time type safety.
        /// The <paramref name="operationName"/> is captured automatically from the calling method
        /// name via <see cref="CallerMemberNameAttribute"/> and used only for log messages.
        /// </summary>
        private async Task<TResponse> InnerHandleWadoRequestAsync<TResponse>(
            DicomWadoRequest request,
            HttpContext context,
            CancellationToken cancellationToken,
            Func<IDicomWadoProvider, DicomWadoRequest, HttpContext, CancellationToken, Task<TResponse>> invoke,
            [CallerMemberName] string operationName = "")
            where TResponse : IDicomWadoResponse
        {
            if (!(this is IDicomWadoProvider thisAsWadoProvider))
            {
                _logger.LogDebug("WADO {Operation} request received but no IDicomWadoProvider is implemented — returning 501", operationName);
                return (TResponse)(IDicomWadoResponse)new DicomWebNotImplementedResponse();
            }

            try
            {
                return await invoke(thisAsWadoProvider, request, context, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "WADO {Operation} request failed: unhandled exception in provider", operationName);
                return (TResponse)(IDicomWadoResponse)new DicomWebUnavailableResponse(e.Message);
            }
        }
    }
}
