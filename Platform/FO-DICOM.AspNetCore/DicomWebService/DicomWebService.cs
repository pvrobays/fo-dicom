using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    public interface IDicomWebService
    {
        /// <summary>Handles a QIDO-RS All Studies search (<c>GET …/studies</c>).</summary>
        Task HandleQidoStudiesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Series search.
        /// <list type="bullet">
        ///   <item><c>GET …/series</c> — all series (no route values needed)</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series</c> — series within a study</item>
        /// </list>
        /// The study scope is extracted automatically from the <c>studyInstanceUID</c> route value
        /// when present in <see cref="HttpRequest.RouteValues"/>.
        /// </summary>
        Task HandleQidoSeriesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Instances search.
        /// <list type="bullet">
        ///   <item><c>GET …/instances</c> — all instances</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/instances</c> — instances within a study</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances</c> — instances within a series</item>
        /// </list>
        /// The study and series scope are extracted automatically from the route values
        /// <c>studyInstanceUID</c> and <c>seriesInstanceUID</c> when present.
        /// </summary>
        Task HandleQidoInstancesRequestAsync(HttpContext context);
    }

    public abstract class DicomWebService : IDicomWebService
    {
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

        public async Task HandleQidoStudiesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var response = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Study, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, cancellationToken);
        }

        public async Task HandleQidoSeriesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var response = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Series, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, cancellationToken);
        }

        public async Task HandleQidoInstancesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            var response = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Image, context, cancellationToken);
            await ExecuteQidoResponseOnHttpContext(context, response, cancellationToken);
        }

        private async Task ExecuteQidoResponseOnHttpContext(HttpContext context, IDicomQidoResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomQidoSuccessResponse successResponse:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json"; //TODO PJ: support XML?
                    await context.Response.WriteAsync(DicomJson.ConvertDicomToJson(
                        successResponse.Results,
                        WriteTagsAsKeywords,
                        FormatJsonIndented
                    ), cancellationToken: cancellationToken);
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

        private async Task<IDicomQidoResponse> InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel level,
            HttpContext context, CancellationToken cancellationToken)
        {
            if (!(this is IDicomQidoProvider thisAsQidoProvider))
            {
                return new DicomQidoNotImplementedResponse();
            }

            DicomQidoRequest request;
            try
            {
                request = QueryToDicomDatasetMapper.Map(level, context.Request.Query);

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
                //TODO PJ: Log exception
                return new DicomQidoBadRequestResponse(e.Message);
            }

            try
            {
                return await thisAsQidoProvider.OnQidoRequestAsync(request, cancellationToken);
            }
            catch (Exception e)
            {
                //TODO PJ: Log exception
                return new DicomQidoUnavailableResponse(e.Message);
            }
        }
    }
}
