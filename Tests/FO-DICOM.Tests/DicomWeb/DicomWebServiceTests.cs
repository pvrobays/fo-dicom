// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore;
using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomWebServiceTests
    {
        #region Helpers

        /// <summary>
        /// Builds a DefaultHttpContext with the given query string parameters.
        /// </summary>
        private static DefaultHttpContext BuildHttpContext(Dictionary<string, StringValues> queryParams = null)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            if (queryParams != null)
            {
                context.Request.Query = new QueryCollection(queryParams);
            }

            return context;
        }

        /// <summary>
        /// Builds a DefaultHttpContext with optional query string parameters and route values.
        /// Route values simulate path parameters from URL templates such as
        /// <c>/studies/{studyInstanceUID}/series</c>.
        /// </summary>
        private static DefaultHttpContext BuildHttpContext(
            Dictionary<string, StringValues> queryParams,
            Dictionary<string, string> routeValues)
        {
            var context = BuildHttpContext(queryParams);
            if (routeValues != null)
            {
                foreach (var kv in routeValues)
                    context.Request.RouteValues[kv.Key] = kv.Value;
            }
            return context;
        }

        /// <summary>
        /// A minimal concrete DicomWebService that implements IDicomQidoProvider,
        /// delegating QIDO handling to an injected callback.
        /// </summary>
        private class TestDicomWebService : DicomWebService, IDicomQidoProvider
        {
            private readonly Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> _handler;

            public TestDicomWebService(Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> handler)
            {
                _handler = handler;
            }

            public Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        /// <summary>
        /// A DicomWebService that does NOT implement IDicomQidoProvider,
        /// to verify that 501 Not Implemented is returned.
        /// </summary>
        private class NotImplementedDicomWebService : DicomWebService
        {
        }

        /// <summary>
        /// A DicomWebService that overrides the JSON formatting properties,
        /// allowing tests to verify the configurable output behaviour.
        /// </summary>
        private class ConfigurableDicomWebService : DicomWebService, IDicomQidoProvider
        {
            private readonly Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> _handler;

            public ConfigurableDicomWebService(
                Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> handler,
                bool writeTagsAsKeywords = false,
                bool formatJsonIndented = false,
                bool strictQueryParameterParsing = true)
            {
                _handler = handler;
                WriteTagsAsKeywordsOverride = writeTagsAsKeywords;
                FormatJsonIndentedOverride = formatJsonIndented;
                StrictQueryParameterParsingOverride = strictQueryParameterParsing;
            }

            private bool WriteTagsAsKeywordsOverride { get; }
            private bool FormatJsonIndentedOverride { get; }
            private bool StrictQueryParameterParsingOverride { get; }

            protected override bool WriteTagsAsKeywords => WriteTagsAsKeywordsOverride;
            protected override bool FormatJsonIndented => FormatJsonIndentedOverride;
            protected override bool StrictQueryParameterParsing => StrictQueryParameterParsingOverride;

            public Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        private static async Task<string> ReadResponseBodyAsync(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        #endregion

        #region 501 Not Implemented (no IDicomQidoProvider)

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_NoQidoProvider_Returns501()
        {
            var service = new NotImplementedDicomWebService();
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        #endregion

        #region 200 OK — successful QIDO responses

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_SuccessfulProvider_Returns200()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_SuccessfulResponse_ContentTypeIsApplicationJson()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.StartsWith("application/json", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_EmptyResults_Returns200WithEmptyJsonArray()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            // An empty result list should produce a valid JSON array "[]"
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_WithResults_Returns200WithNonEmptyJsonArray()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.PatientID, "12345");
            dataset.Add(DicomTag.StudyInstanceUID, DicomUID.Generate());

            var response = new DicomQidoSuccessResponse();
            response.AddResult(dataset);

            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(response));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
        }

        #endregion

        #region Failure responses — HTTP status codes

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_BadRequestResponse_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoBadRequestResponse("bad")));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_UnauthorizedResponse_Returns401()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoUnauthorizedResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_ForbiddenResponse_Returns403()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoForbiddenResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_RequestTooBroadResponse_Returns413()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoRequestTooBroadResponse()));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status413RequestEntityTooLarge, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_UnavailableResponse_Returns503()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoUnavailableResponse("down")));
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        #endregion

        #region Query parameter parsing — reserved parameters

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_FuzzyMatchingTrue_SetsOptionOnRequest()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["fuzzymatching"] = "true"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Options.IsFuzzyMatching);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_FuzzyMatchingFalse_SetsOptionOnRequest()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["fuzzymatching"] = "false"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest.Options.IsFuzzyMatching);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_LimitParameter_SetsLimitOnRequest()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["limit"] = "25"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(25, capturedRequest.Options.Limit);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_OffsetParameter_SetsOffsetOnRequest()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["offset"] = "10"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(10, capturedRequest.Options.Offset);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_NegativeOffset_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["offset"] = "-1"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_InvalidLimit_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["limit"] = "notanumber"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        #endregion

        #region Query parameter parsing — DICOM attribute matching

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_PatientIdByKeyword_AddedToDataset()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["PatientID"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.PatientID));
            Assert.Equal("11235813", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_PatientIdByHexTag_AddedToDataset()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            // PatientID = (0010,0020) -> "00100020"
            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["00100020"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("11235813", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_MultipleMatchParams_AllAddedToDataset()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["PatientID"] = "11235813",
                ["StudyDate"] = "20130509"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("11235813", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            Assert.Equal("20130509", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_UnknownQueryParam_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["NotADicomKeyword"] = "somevalue"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        #endregion

        #region includefield parameter

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldByKeyword_AddedToDataset()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "ReferringPhysicianName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldByHexTag_AddedToDataset()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            // ReferringPhysicianName = (0008,0090) -> "00080090"
            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "00080090"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldCsvMultipleTags_AllAddedToDataset()
        {
            // Per PS3.18 sect_10.6.1.2:
            // /studies?PatientID=11235813&includefield=00081048,00081049,00081060
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "00080090,00100010" // ReferringPhysicianName, PatientName
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.PatientName));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldMultipleSeparateParams_AllAddedToDataset()
        {
            // Per PS3.18 sect_10.6.1.2:
            // /studies?PatientID=11235813&includefield=00081048&includefield=00081049&includefield=00081060
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = new StringValues(new[] { "00080090", "00100010" }) // multiple values
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.PatientName));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldUnknownTag_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "NotADicomKeyword"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldIsNotAddedToMatchingQuery()
        {
            // Reserved params (includefield) must not appear as match keys in the dataset
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "PatientID",
                ["PatientID"] = "12345"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            // PatientID should be set as a match value, not just empty from includefield
            Assert.NotNull(capturedRequest);
            Assert.Equal("12345", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        #endregion

        #region Sequence support — includefield with dot notation

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldDotNotationHexTags_CreatesNestedSequence()
        {
            // includefield=00081115.00080060  (ReferencedSeriesSequence.Modality)
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "00081115.00080060"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.ReferencedSeriesSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.True(seq.Items[0].Contains(DicomTag.Modality));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldDotNotationKeywords_CreatesNestedSequence()
        {
            // includefield=OtherPatientIDsSequence.PatientID
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "OtherPatientIDsSequence.PatientID"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.True(seq.Items[0].Contains(DicomTag.PatientID));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldDotNotationMixedHexAndKeyword_CreatesNestedSequence()
        {
            // includefield=00101002.PatientID  (hex sequence tag, keyword leaf tag)
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "00101002.PatientID"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            // (0010,1002) = OtherPatientIDsSequence
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.True(seq.Items[0].Contains(DicomTag.PatientID));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldDotNotation_InvalidSegment_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "NotATag.PatientID"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldDotNotation_NonSqIntermediateSegment_Returns400()
        {
            // PatientID is not an SQ tag, so using it as an intermediate segment should fail
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "PatientID.PatientName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        #endregion

        #region Sequence support — includefield with bare SQ tag

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldBareSqTag_AddsEmptySequence()
        {
            // includefield=RequestAttributesSequence
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "RequestAttributesSequence"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.Empty(seq.Items);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldBareSqTagByHex_AddsEmptySequence()
        {
            // includefield=00400275  (RequestAttributesSequence)
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "00400275"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.Empty(seq.Items);
        }

        #endregion

        #region Sequence support — query param with dot notation

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamDotNotationHexTags_CreatesNestedFilter()
        {
            // ?00101002.00100020=11235813  (OtherPatientIDsSequence.PatientID=11235813)
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["00101002.00100020"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamDotNotationKeywords_CreatesNestedFilter()
        {
            // ?OtherPatientIDsSequence.PatientID=11235813
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["OtherPatientIDsSequence.PatientID"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamDotNotation_InvalidSegment_Returns400()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["NotATag.PatientID"] = "value"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamDotNotation_NonSqIntermediateSegment_Returns400()
        {
            // PatientID (LO VR) used as intermediate sequence tag -> error
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["PatientID.PatientName"] = "value"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        #endregion

        #region Sequence support — query param with bare SQ tag (treated as include field)

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamBareSqTag_TreatedAsIncludeField()
        {
            // ?RequestAttributesSequence=somevalue  -> bare SQ tag, treated as include field
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["RequestAttributesSequence"] = "somevalue"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq));
            Assert.Empty(seq.Items);
        }

        #endregion

        #region Sequence support — deep nesting (3+ levels)

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_QueryParamThreeLevelDotNotation_CreatesDeepNestedFilter()
        {
            // RequestAttributesSequence -> ReferencedStudySequence -> PatientID
            // (0040,0275).(0008,1110).(0010,0020)=DEEP_VALUE
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["00400275.00081110.00100020"] = "DEEP_VALUE"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            // Level 1: RequestAttributesSequence
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq1));
            Assert.Single(seq1.Items);
            // Level 2: ReferencedStudySequence
            Assert.True(seq1.Items[0].TryGetSequence(DicomTag.ReferencedStudySequence, out var seq2));
            Assert.Single(seq2.Items);
            // Level 3: PatientID leaf
            Assert.Equal("DEEP_VALUE", seq2.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldThreeLevelDotNotation_CreatesDeepNestedInclude()
        {
            // includefield=RequestAttributesSequence.ReferencedStudySequence.PatientID
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "RequestAttributesSequence.ReferencedStudySequence.PatientID"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.RequestAttributesSequence, out var seq1));
            Assert.Single(seq1.Items);
            Assert.True(seq1.Items[0].TryGetSequence(DicomTag.ReferencedStudySequence, out var seq2));
            Assert.Single(seq2.Items);
            Assert.True(seq2.Items[0].Contains(DicomTag.PatientID));
        }

        #endregion

        #region Sequence support — multiple dot-notation params sharing same parent sequence

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_TwoDotNotationParamsSameParentSequence_MergedIntoSameSequenceItem()
        {
            // ?OtherPatientIDsSequence.PatientID=11235813&OtherPatientIDsSequence.PatientName=SMITH
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["OtherPatientIDsSequence.PatientID"] = "11235813",
                ["OtherPatientIDsSequence.PatientName"] = "SMITH"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            // Both attributes should be in the same single sequence item
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            Assert.Equal("SMITH", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAndQueryParamSameSequence_MergedIntoSameSequenceItem()
        {
            // ?OtherPatientIDsSequence.PatientID=11235813&includefield=OtherPatientIDsSequence.PatientName
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["OtherPatientIDsSequence.PatientID"] = "11235813",
                ["includefield"] = "OtherPatientIDsSequence.PatientName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            // Filter value
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            // Include field (empty value)
            Assert.True(seq.Items[0].Contains(DicomTag.PatientName));
        }

        #endregion

        #region Sequence support — combined with standard DICOM spec examples

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_DicomSpecExample_PatientNameAndSequenceFilter()
        {
            // From PS3.18 sect_10.6.1.2:
            // /studies?00100010=SMITH*&00101002.00100020=11235813
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["00100010"] = "SMITH*",
                ["00101002.00100020"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            // Top-level filter
            Assert.Equal("SMITH*", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
            // Sequence filter
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_DicomSpecExample_KeywordSequenceFilter()
        {
            // From PS3.18 sect_10.6.1.2:
            // /studies?00100010=SMITH*&OtherPatientIDsSequence.00100020=11235813
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["00100010"] = "SMITH*",
                ["OtherPatientIDsSequence.00100020"] = "11235813"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("SMITH*", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
            Assert.True(capturedRequest.Dataset.TryGetSequence(DicomTag.OtherPatientIDsSequence, out var seq));
            Assert.Single(seq.Items);
            Assert.Equal("11235813", seq.Items[0].GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        #endregion

        #region includefield=all

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_Returns200()
        {
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "all"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_SetsIncludeAllFieldsTrue()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "all"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.IncludeAllFields);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_CaseInsensitive()
        {
            // Per DICOM spec the keyword is lowercase "all", but be lenient
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "ALL"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.IncludeAllFields);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_WithMatchParams_Returns200()
        {
            // Match params (e.g. PatientID=12345) are orthogonal to includefield=all
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "all",
                ["PatientID"] = "12345"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.IncludeAllFields);
            Assert.Equal("12345", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_WithOtherCsvField_Returns400()
        {
            // includefield=all,PatientName — "all" must not be combined with other includefield values
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "all,PatientName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_IncludeFieldAll_WithOtherSeparateParam_Returns400()
        {
            // includefield=all&includefield=PatientName — strict: "all" must be the only includefield
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                // ASP.NET Core merges repeated includefield keys into a multi-value StringValues
                ["includefield"] = new StringValues(new[] { "all", "PatientName" })
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_DefaultRequest_IncludeAllFieldsIsFalse()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest.IncludeAllFields);
        }

        #endregion

        #region Regression — existing includefield and match still work

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StandardMatchAndIncludeField_StillWorkTogether()
        {
            // Ensure adding sequence support didn't break basic non-sequence behavior
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["PatientID"] = "11235813",
                ["includefield"] = "ReferringPhysicianName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("11235813", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        #endregion

        #region Request level


        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_RequestIsStudyLevel()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(DicomQueryRetrieveLevel.Study, capturedRequest.Level);
        }

        #endregion

        #region Series and Instance endpoints (PS3.18 Table 10.6.1-1)

        // ── HandleQidoSeriesRequestAsync ──────────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_NoRouteValues_RequestIsSeriesLevel()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            await service.HandleQidoSeriesRequestAsync(BuildHttpContext());

            Assert.NotNull(capturedRequest);
            Assert.Equal(DicomQueryRetrieveLevel.Series, capturedRequest.Level);
        }

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_WithStudyInstanceUID_InjectedIntoDataset()
        {
            // Simulates GET /studies/{studyInstanceUID}/series — the route value should be
            // injected into the dataset as a StudyInstanceUID match constraint.
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(null, new Dictionary<string, string>
            {
                ["studyInstanceUID"] = "1.2.3.4.5"
            });

            await service.HandleQidoSeriesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_NoRouteValues_StudyInstanceUIDNotConstrained()
        {
            // Simulates GET /series — relational query, no study scope
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            await service.HandleQidoSeriesRequestAsync(BuildHttpContext());

            Assert.NotNull(capturedRequest);
            // StudyInstanceUID may be present (seeded empty by constructor) but must not be set to a real UID
            Assert.Equal(string.Empty,
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_WithQueryParam_QueryParamApplied()
        {
            // Regular query params still work for series endpoints
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(
                new Dictionary<string, StringValues> { ["Modality"] = "CT" },
                new Dictionary<string, string> { ["studyInstanceUID"] = "1.2.3.4.5" });

            await service.HandleQidoSeriesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("CT",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty));
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_RouteStudyUID_OverridesQueryStringStudyUID()
        {
            // Route value takes precedence over any query-string value for the same tag
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(
                new Dictionary<string, StringValues> { ["StudyInstanceUID"] = "9.9.9" },
                new Dictionary<string, string> { ["studyInstanceUID"] = "1.2.3.4.5" });

            await service.HandleQidoSeriesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            // Route value "1.2.3.4.5" must win over query-string "9.9.9"
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        // ── HandleQidoInstancesRequestAsync ───────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_NoRouteValues_RequestIsImageLevel()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            await service.HandleQidoInstancesRequestAsync(BuildHttpContext());

            Assert.NotNull(capturedRequest);
            Assert.Equal(DicomQueryRetrieveLevel.Image, capturedRequest.Level);
        }

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_WithStudyUID_InjectedIntoDataset()
        {
            // Simulates GET /studies/{studyInstanceUID}/instances
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(null, new Dictionary<string, string>
            {
                ["studyInstanceUID"] = "1.2.3.4.5"
            });

            await service.HandleQidoInstancesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            // No series constraint injected
            Assert.Equal(string.Empty,
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_WithStudyAndSeriesUID_BothInjectedIntoDataset()
        {
            // Simulates GET /studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(null, new Dictionary<string, string>
            {
                ["studyInstanceUID"] = "1.2.3.4.5",
                ["seriesInstanceUID"] = "6.7.8.9.0"
            });

            await service.HandleQidoInstancesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal("6.7.8.9.0",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_NoRouteValues_NeitherUidConstrained()
        {
            // Simulates GET /instances — relational query, no study or series scope
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            await service.HandleQidoInstancesRequestAsync(BuildHttpContext());

            Assert.NotNull(capturedRequest);
            Assert.Equal(string.Empty,
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal(string.Empty,
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_WithQueryParams_QueryParamsApplied()
        {
            // Query params still work on instance endpoints
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(
                new Dictionary<string, StringValues> { ["SOPClassUID"] = "1.2.840.10008.5.1.4.1.1.2" },
                new Dictionary<string, string>
                {
                    ["studyInstanceUID"] = "1.2.3.4.5",
                    ["seriesInstanceUID"] = "6.7.8.9.0"
                });

            await service.HandleQidoInstancesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("1.2.840.10008.5.1.4.1.1.2",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, string.Empty));
        }

        // ── 501 Not Implemented on new methods ────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoSeriesRequest_NoProvider_Returns501()
        {
            var service = new NotImplementedDicomWebService();
            var context = BuildHttpContext();
            await service.HandleQidoSeriesRequestAsync(context);
            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoInstancesRequest_NoProvider_Returns501()
        {
            var service = new NotImplementedDicomWebService();
            var context = BuildHttpContext();
            await service.HandleQidoInstancesRequestAsync(context);
            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        #endregion

        #region UID list matching (PS3.18 Section 8.3.4.1 / PS3.4 C.2.2.2.2)

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyInstanceUID_SingleUid_StoredAsSingleValue()
        {
            // A single UID (no comma) behaves exactly as before
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyInstanceUID"] = "1.2.3.4.5"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(1, capturedRequest.Dataset.GetValueCount(DicomTag.StudyInstanceUID));
            Assert.Equal("1.2.3.4.5",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyInstanceUID_TwoUids_StoredAsTwoValues()
        {
            // "1.2.3,4.5.6" → two separate UID values in the dataset per PS3.4 C.2.2.2.2
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyInstanceUID"] = "1.2.3,4.5.6"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(2, capturedRequest.Dataset.GetValueCount(DicomTag.StudyInstanceUID));
            var uids = capturedRequest.Dataset.GetValues<string>(DicomTag.StudyInstanceUID);
            Assert.Contains("1.2.3", uids);
            Assert.Contains("4.5.6", uids);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyInstanceUID_ThreeUids_StoredAsThreeValues()
        {
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyInstanceUID"] = "1.2.3,4.5.6,7.8.9"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(3, capturedRequest.Dataset.GetValueCount(DicomTag.StudyInstanceUID));
            var uids = capturedRequest.Dataset.GetValues<string>(DicomTag.StudyInstanceUID);
            Assert.Contains("1.2.3", uids);
            Assert.Contains("4.5.6", uids);
            Assert.Contains("7.8.9", uids);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyInstanceUID_ViaHexTag_TwoUids_StoredAsTwoValues()
        {
            // Same behaviour when the tag is specified as a hex string (0020000D)
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["0020000D"] = "1.2.3,4.5.6"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(2, capturedRequest.Dataset.GetValueCount(DicomTag.StudyInstanceUID));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_SOPInstanceUID_TwoUids_StoredAsTwoValues()
        {
            // Confirms the generic VR-based approach applies to all UI tags, not just StudyInstanceUID
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["SOPInstanceUID"] = "1.2.3,4.5.6"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(2, capturedRequest.Dataset.GetValueCount(DicomTag.SOPInstanceUID));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_NonUidTagWithComma_StoredAsRawString()
        {
            // A non-UI tag whose value contains a comma must NOT be split.
            // AccessionNumber (SH VR) with value "ACC1,ACC2" should be stored verbatim.
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["AccessionNumber"] = "ACC1,ACC2"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal(1, capturedRequest.Dataset.GetValueCount(DicomTag.AccessionNumber));
            Assert.Equal("ACC1,ACC2",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty));
        }

        #endregion

        #region Date range matching (PS3.4 C.2.2.2.5)

        // ── DA (Date) range matching ───────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyDate_BoundedRange_StoredAsDateRange()
        {
            // "20130101-20131231" → DicomDateRange with correct min/max
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyDate"] = "20130101-20131231"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            var range = capturedRequest.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(new DateTime(2013, 1, 1), range.Minimum);
            Assert.Equal(new DateTime(2013, 12, 31), range.Maximum);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyDate_OpenStartRange_MinIsMinValue()
        {
            // "-20131231" → open start: all dates up to and including 2013-12-31
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyDate"] = "-20131231"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            var range = capturedRequest.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(DateTime.MinValue, range.Minimum);
            Assert.Equal(new DateTime(2013, 12, 31), range.Maximum);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyDate_OpenEndRange_MaxIsMaxValue()
        {
            // "20130101-" → open end: all dates from 2013-01-01 onwards
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyDate"] = "20130101-"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            var range = capturedRequest.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
            Assert.Equal(new DateTime(2013, 1, 1), range.Minimum);
            Assert.Equal(DateTime.MaxValue, range.Maximum);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyDate_SingleDate_StoredAsRawString()
        {
            // Single date (no hyphen) stays as a raw string — no change from existing behaviour
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyDate"] = "20130509"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("20130509",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty));
        }

        // ── TM (Time) range matching ───────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StudyTime_BoundedRange_StoredAsDateRange()
        {
            // "090000-170000" → DicomDateRange with correct hour bounds
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["StudyTime"] = "090000-170000"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            var range = capturedRequest.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyTime);
            Assert.Equal(9, range.Minimum.Hour);
            Assert.Equal(0, range.Minimum.Minute);
            Assert.Equal(17, range.Maximum.Hour);
            Assert.Equal(0, range.Maximum.Minute);
        }

        // ── DT (DateTime) range matching ──────────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_AcquisitionDateTime_BoundedRange_StoredAsDateRange()
        {
            // "20130101000000-20131231235959" → full datetime range
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["AcquisitionDateTime"] = "20130101000000-20131231235959"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            var range = capturedRequest.Dataset.GetSingleValue<DicomDateRange>(DicomTag.AcquisitionDateTime);
            Assert.Equal(new DateTime(2013, 1, 1, 0, 0, 0), range.Minimum);
            Assert.Equal(new DateTime(2013, 12, 31, 23, 59, 59), range.Maximum);
        }

        // ── Non-date tag is unaffected ─────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_NonDateTagWithHyphen_StoredAsRawString()
        {
            // A non-DA/TM/DT tag whose value happens to contain a hyphen (e.g. PatientName
            // with "SMITH-JONES") must NOT be misidentified as a date range.
            DicomQidoRequest capturedRequest = null;
            var service = new TestDicomWebService((req, ct) =>
            {
                capturedRequest = req;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            });

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["PatientName"] = "SMITH-JONES"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.NotNull(capturedRequest);
            Assert.Equal("SMITH-JONES",
                capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        #endregion

        #region JSON output formatting — WriteTagsAsKeywords and FormatJsonIndented

        private static DicomQidoSuccessResponse MakeSuccessResponseWithOneDataset()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.PatientID, "12345");
            var response = new DicomQidoSuccessResponse();
            response.AddResult(dataset);
            return response;
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_DefaultService_JsonKeysAreHexTags()
        {
            // Default: WriteTagsAsKeywords=false → standard-compliant 8-char hex keys per PS3.18 F.2.2
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(MakeSuccessResponseWithOneDataset()));

            var context = BuildHttpContext();
            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            // PatientID = (0010,0020) → "00100020"
            Assert.Contains("\"00100020\"", body);
            Assert.DoesNotContain("\"PatientID\"", body);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_WriteTagsAsKeywordsTrue_JsonKeysAreKeywords()
        {
            var service = new ConfigurableDicomWebService(
                (req, ct) => Task.FromResult<IDicomQidoResponse>(MakeSuccessResponseWithOneDataset()),
                writeTagsAsKeywords: true);

            var context = BuildHttpContext();
            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            Assert.Contains("\"PatientID\"", body);
            Assert.DoesNotContain("\"00100020\"", body);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_DefaultService_JsonIsCompact()
        {
            // Default: FormatJsonIndented=false → no newlines in the JSON body
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(MakeSuccessResponseWithOneDataset()));

            var context = BuildHttpContext();
            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            Assert.DoesNotContain("\n", body);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_FormatJsonIndentedTrue_JsonContainsNewlines()
        {
            var service = new ConfigurableDicomWebService(
                (req, ct) => Task.FromResult<IDicomQidoResponse>(MakeSuccessResponseWithOneDataset()),
                formatJsonIndented: true);

            var context = BuildHttpContext();
            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            Assert.Contains("\n", body);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_WriteTagsAsKeywordsAndIndented_BothApplied()
        {
            var service = new ConfigurableDicomWebService(
                (req, ct) => Task.FromResult<IDicomQidoResponse>(MakeSuccessResponseWithOneDataset()),
                writeTagsAsKeywords: true,
                formatJsonIndented: true);

            var context = BuildHttpContext();
            await service.HandleQidoStudiesRequestAsync(context);

            var body = await ReadResponseBodyAsync(context);
            Assert.Contains("\"PatientID\"", body);
            Assert.Contains("\n", body);
        }

        #endregion

        #region StrictQueryParameterParsing

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StrictMode_UnknownParam_Returns400()
        {
            // Default behaviour: unknown query param → 400
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["NotADicomTag"] = "value"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_LenientMode_UnknownParam_Returns200()
        {
            // Lenient mode: unknown query param is skipped → 200
            DicomQidoRequest capturedRequest = null;
            var service = new ConfigurableDicomWebService(
                (req, ct) =>
                {
                    capturedRequest = req;
                    return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
                },
                strictQueryParameterParsing: false);

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["NotADicomTag"] = "shouldBeIgnored",
                ["PatientID"] = "12345"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(capturedRequest);
            // The valid param was still applied
            Assert.Equal("12345", capturedRequest.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_LenientMode_UnknownIncludeField_Returns200()
        {
            // Lenient mode: unknown includefield is skipped → 200; valid includefield still applied
            DicomQidoRequest capturedRequest = null;
            var service = new ConfigurableDicomWebService(
                (req, ct) =>
                {
                    capturedRequest = req;
                    return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
                },
                strictQueryParameterParsing: false);

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["includefield"] = "NotADicomTag,ReferringPhysicianName"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Dataset.Contains(DicomTag.ReferringPhysicianName));
        }

        [FactForNetCore]
        public async Task HandleQidoStudiesRequest_StrictMode_IsDefault()
        {
            // TestDicomWebService does not override StrictQueryParameterParsing, so it defaults to true
            var service = new TestDicomWebService((req, ct) =>
                Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse()));

            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["NotADicomTag"] = "value"
            });

            await service.HandleQidoStudiesRequestAsync(context);

            // Strict mode (default) → 400
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        #endregion
    }
}

#endif
