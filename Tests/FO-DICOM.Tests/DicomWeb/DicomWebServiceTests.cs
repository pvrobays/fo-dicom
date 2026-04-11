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
        /// A minimal concrete DicomWebService that implements IDicomQidoProvider,
        /// delegating QIDO handling to an injected callback.
        /// </summary>
        private class TestDicomWebService : DicomWebService, IDicomQidoProvider
        {
            private readonly System.Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> _handler;

            public TestDicomWebService(System.Func<DicomQidoRequest, CancellationToken, Task<IDicomQidoResponse>> handler)
            {
                _handler = handler;
            }

            public Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        /// <summary>
        /// A DicomWebService that does NOT implement IDicomQidoProvider,
        /// to verify that 501 Not Implemented is returned.
        /// </summary>
        private class NotImplementedDicomWebService : DicomWebService
        {
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
    }
}

#endif
