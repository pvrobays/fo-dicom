using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebServer
{
    public interface IDicomWebServer
    {
        Task HandleQidoStudiesRequestAsync(HttpContext context);
    }

    //TODO PJ: rename to DicomWebService to be in line with DIMSE implementation?
    public abstract class DicomWebServer : IDicomWebServer
    {
        public async Task HandleQidoStudiesRequestAsync(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;

            var response = await InnerHandleQidoRequestAsync(DicomQueryRetrieveLevel.Study, context, cancellationToken);

            await ExecuteQidoResponseOnHttpContext(context, response, cancellationToken);
        }

        private static async Task ExecuteQidoResponseOnHttpContext(HttpContext context, IDicomQidoResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                //Success
                case DicomQidoSuccessResponse successResponse:
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json"; //TODO PJ: support XML? 
                    await context.Response.WriteAsync(DicomJson.ConvertDicomToJson(
                        successResponse.Results,
                        true,
                        true
                    ), cancellationToken: cancellationToken);
                    break;

                //Failure
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
            //Check if we support QIDO-RS
            if (!(this is IDicomQidoProvider thisAsQidoProvider))
            {
                return new DicomQidoNotImplementedResponse();
            }

            DicomQidoRequest request;
            try
            {
                //Map the request
                request = MapDicomQidoRequest(level, context.Request);
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

        private static DicomQidoRequest MapDicomQidoRequest(DicomQueryRetrieveLevel level, HttpRequest httpRequest)
        {
            var isFuzzyMatching = ParseBoolean(httpRequest.Query, "fuzzymatching");
            if (!TryParseInt(httpRequest.Query, "limit", out var limit))
            {
                throw new InvalidOperationException("Invalid limit value");
            }

            if (!TryParseInt(httpRequest.Query, "offset", out var offset))
            {
                throw new InvalidOperationException("Invalid offset value");
            }

            if (offset < 0)
            {
                throw new InvalidOperationException("Offset must be greater than or equal to 0");
            }

            var qidoRequestOptions = new DicomQidoRequestOptions(isFuzzyMatching, limit, offset);

            var dicomRequest = new DicomQidoRequest(level, qidoRequestOptions);

            //TODO PJ: map the DICOM Dataset from the query parameters and level
            var dataset = dicomRequest.Dataset;
            foreach (var (key, stringValues) in httpRequest.Query)
            {
                //TODO PJ: move this to its own class `QueryToDicomDatasetMapper`?
                //try to map the key to a DICOM tag
                //TODO PJ: support sequences!
                var dicomTag = ParseTag(key); //TODO PJ: try parse?

                //TODO PJ: filter non-queryable tags?

                //map the value to a DICOM value linked to that DICOM tag
                dataset.AddOrUpdate(dicomTag,
                    stringValues.ToArray()); //TODO PJ: check that every value works as a string?
            }

            return dicomRequest;
        }

        private static DicomTag ParseTag(string tagString)
        {
            if (Regex.IsMatch(tagString, @"\A\b[0-9a-fA-F]+\b\Z"))
            {
                var group = Convert.ToUInt16(tagString[..4], 16);
                var element = Convert.ToUInt16(tagString[4..], 16);
                var tag = new DicomTag(group, element);
                return tag;
            }

            return DicomDictionary.Default[tagString];
        }

        private static bool TryParseInt(IQueryCollection query, string key, out int o)
        {
            if (!query.ContainsKey(key))
            {
                o = 0;
                return true;
            }

            return int.TryParse(query[key], out o);
        }

        private static bool ParseBoolean(IQueryCollection query, string key)
        {
            if (!query.ContainsKey(key))
            {
                return false;
            }

            return query[key].ToString().ToLowerInvariant() == "true";
        }
    }
}