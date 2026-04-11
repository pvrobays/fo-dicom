using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    public interface IDicomWebService
    {
        Task HandleQidoStudiesRequestAsync(HttpContext context);
    }

    public abstract class DicomWebService : IDicomWebService
    {

        private static readonly string[] _reservedQidoParameters = {
            "fuzzymatching",
            "limit",
            "offset"
        };

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

        private async Task ExecuteQidoResponseOnHttpContext(HttpContext context, IDicomQidoResponse response,
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
                        WriteTagsAsKeywords,
                        FormatJsonIndented
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
            foreach ((string key, StringValues stringValues) in httpRequest.Query)
            {
                if (_reservedQidoParameters.Contains(key))
                {
                    continue;
                }

                if (key.Equals("includefield", StringComparison.OrdinalIgnoreCase))
                {
                    var includeValues = stringValues.SelectMany(sv => sv.Split(',')).ToList();

                    // Per PS3.18 Section 8.3.4.3: "all" is mutually exclusive with other includefield values
                    var hasAll = includeValues.Any(v => v.Equals("all", StringComparison.OrdinalIgnoreCase));
                    if (hasAll)
                    {
                        if (includeValues.Count > 1)
                        {
                            throw new InvalidOperationException(
                                "includefield=all must not be combined with other includefield values (PS3.18 Section 8.3.4.3)");
                        }
                        dicomRequest.IncludeAllFields = true;
                        continue;
                    }

                    foreach (string value in includeValues)
                    {
                        if (value.Contains('.'))
                        {
                            // Dot notation: e.g. "00081115.00080060" or "OtherPatientIDsSequence.PatientID"
                            var path = ParseAttributePath(value);
                            AddNestedAttribute(dataset, path, new[] { string.Empty });
                            continue;
                        }
                        
                        if (!DicomTag.TryParseByKeywordOrTag(value, out var includeTag))
                        {
                            //TODO PJ: Log that the key could not be mapped to a DICOM tag
                            //TODO PJ: depending on a setting, throw or just continue?
                            throw new InvalidOperationException($"Could not map includefield '{value}' to a DICOM tag");
                        }
                        if (includeTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                        {
                            // SQ-typed tag without dot notation: add an empty sequence to the dataset
                            dataset.AddOrUpdate(new DicomSequence(includeTag));
                            continue;
                        }
                        dataset.AddOrUpdate(includeTag, string.Empty);
                    }
                    
                    continue;
                }
                
                //TODO PJ: filter non-queryable tags?
                
                //TODO PJ: move this to its own class `QueryToDicomDatasetMapper`?
                //try to map the key to a DICOM tag

                if (key.Contains('.'))
                {
                    // Dot notation: e.g. "00081115.00080060=CT" or "OtherPatientIDsSequence.PatientID=11235813"
                    var path = ParseAttributePath(key);
                    AddNestedAttribute(dataset, path, stringValues.ToArray());
                    continue;
                }
                
                if (!DicomTag.TryParseByKeywordOrTag(key, out var dicomTag))
                {
                    //TODO PJ: Log that the key could not be mapped to a DICOM tag
                    //TODO PJ: depending on a setting, throw or just continue?
                    throw new InvalidOperationException($"Could not map query parameter '{key}' to a DICOM tag");
                    // continue;
                }

                //map the value to a DICOM value linked to that DICOM tag
                if (dicomTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                {
                    // Bare SQ-typed tag without dot notation: treat as include field (add empty sequence)
                    dataset.AddOrUpdate(new DicomSequence(dicomTag));
                    continue;
                }
                dataset.AddOrUpdate(dicomTag, stringValues.ToArray()); //TODO PJ: check that every value works as a string?
                //TODO PJ: add study date ranges?
                //TODO PJ: add ability for multiple values (e.g. study instance UID list with csv)
            }

            return dicomRequest;
        }

        /// <summary>
        /// Parses a dot-notation attribute path (e.g. "00081115.00080060" or
        /// "OtherPatientIDsSequence.PatientID") into an ordered array of DicomTags.
        /// Each segment can be a hex tag (8 hex digits) or a keyword.
        /// All segments except the last must be sequence (SQ) tags.
        /// </summary>
        private static DicomTag[] ParseAttributePath(string attributePath)
        {
            var segments = attributePath.Split('.');
            if (segments.Length < 2)
            {
                throw new InvalidOperationException(
                    $"Attribute path '{attributePath}' must contain at least two segments separated by '.'");
            }

            var tags = new DicomTag[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!DicomTag.TryParseByKeywordOrTag(segments[i], out var tag))
                {
                    throw new InvalidOperationException(
                        $"Could not parse segment '{segments[i]}' in attribute path '{attributePath}' as a DICOM tag");
                }

                // All segments except the last must be sequence tags
                if (i < segments.Length - 1 && !tag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                {
                    throw new InvalidOperationException(
                        $"Segment '{segments[i]}' ({tag.DictionaryEntry.Keyword}) in attribute path '{attributePath}' is not a sequence tag");
                }

                tags[i] = tag;
            }

            return tags;
        }

        /// <summary>
        /// Adds a nested attribute to the dataset following a sequence path.
        /// For a path [SeqTagA, SeqTagB, LeafTag] with values ["CT"], this builds:
        ///   SeqTagA -> DicomSequence containing one item ->
        ///     SeqTagB -> DicomSequence containing one item ->
        ///       LeafTag = "CT"
        /// If a sequence already exists at any level, the leaf attribute is merged into
        /// the first existing sequence item rather than creating a duplicate sequence.
        /// </summary>
        private static void AddNestedAttribute(DicomDataset dataset, DicomTag[] path, string[] values)
        {
            // Navigate/create the sequence chain for all tags except the last (the leaf)
            var currentDataset = dataset;
            for (int i = 0; i < path.Length - 1; i++)
            {
                var seqTag = path[i];
                DicomSequence sequence;
                if (currentDataset.TryGetSequence(seqTag, out var existingSequence) && existingSequence.Items.Count > 0)
                {
                    // Reuse the first item of the existing sequence
                    sequence = existingSequence;
                }
                else
                {
                    // Create a new sequence with one empty item
                    sequence = new DicomSequence(seqTag, new DicomDataset().NotValidated());
                    currentDataset.AddOrUpdate(sequence);
                }

                currentDataset = sequence.Items[0];
            }

            // Add the leaf tag with its value(s)
            var leafTag = path[path.Length - 1];
            if (leafTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
            {
                // Leaf is itself a sequence: add an empty sequence
                currentDataset.AddOrUpdate(new DicomSequence(leafTag));
            }
            else if (values.Length == 1)
            {
                // Single value (or empty string for include fields) —
                // use the single-string overload to match the non-sequence code path
                currentDataset.AddOrUpdate(leafTag, values[0]);
            }
            else
            {
                currentDataset.AddOrUpdate(leafTag, values);
            }
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