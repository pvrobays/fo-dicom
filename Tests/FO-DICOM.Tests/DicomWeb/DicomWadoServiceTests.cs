// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomWadoServiceTests
    {
        #region Test doubles

        /// <summary>
        /// A concrete DicomWebService implementing both QIDO and WADO providers.
        /// Both handlers are delegate-based for test flexibility.
        /// </summary>
        private class TestWadoService : DicomWebService, IDicomWadoProvider
        {
            private readonly Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> _instancesHandler;
            private readonly Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoMetadataResponse>> _metadataHandler;

            public TestWadoService(
                Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> instancesHandler,
                Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoMetadataResponse>> metadataHandler = null)
            {
                _instancesHandler = instancesHandler;
                _metadataHandler = metadataHandler
                    ?? ((req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>())));
            }

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _instancesHandler(request, cancellationToken);

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _metadataHandler(request, cancellationToken);
        }

        /// <summary>
        /// A DicomWebService that does NOT implement IDicomWadoProvider,
        /// used to verify that 501 Not Implemented is returned for WADO requests.
        /// </summary>
        private class WadoNotImplementedService : DicomWebService
        {
        }

        private static DefaultHttpContext BuildHttpContext(string acceptHeader = null)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            if (acceptHeader != null)
            {
                context.Request.Headers["Accept"] = acceptHeader;
            }
            return context;
        }

        private static DefaultHttpContext BuildHttpContextWithRouteValues(
            string studyUid = null,
            string seriesUid = null,
            string sopUid = null,
            string acceptHeader = null)
        {
            var context = BuildHttpContext(acceptHeader);
            if (studyUid != null)  context.Request.RouteValues["studyInstanceUID"]  = studyUid;
            if (seriesUid != null) context.Request.RouteValues["seriesInstanceUID"] = seriesUid;
            if (sopUid != null)    context.Request.RouteValues["sopInstanceUID"]    = sopUid;
            return context;
        }

        private static async Task<string> ReadBodyAsync(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        private static async Task<byte[]> ReadBodyBytesAsync(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var ms = new MemoryStream();
            await context.Response.Body.CopyToAsync(ms);
            return ms.ToArray();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────────
        // 501 Not Implemented (no IDicomWadoProvider)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_NoWadoProvider_Returns501()
        {
            var service = new WadoNotImplementedService();
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");

            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_NoWadoProvider_Returns501()
        {
            var service = new WadoNotImplementedService();
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");

            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Route UID injection into DicomWadoRequest
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_StudyLevel_InjectsOnlyStudyUid()
        {
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.Equal("1.2.3", captured.StudyInstanceUid);
            Assert.Null(captured.SeriesInstanceUid);
            Assert.Null(captured.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_SeriesLevel_InjectsStudyAndSeriesUid()
        {
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", seriesUid: "4.5.6");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.Equal("1.2.3", captured.StudyInstanceUid);
            Assert.Equal("4.5.6", captured.SeriesInstanceUid);
            Assert.Null(captured.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_InstanceLevel_InjectsAllThreeUids()
        {
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", seriesUid: "4.5.6", sopUid: "7.8.9");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.Equal("1.2.3", captured.StudyInstanceUid);
            Assert.Equal("4.5.6", captured.SeriesInstanceUid);
            Assert.Equal("7.8.9", captured.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_SeriesLevel_InjectsStudyAndSeriesUid()
        {
            DicomWadoRequest captured = null;
            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) =>
                {
                    captured = req;
                    return Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
                });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", seriesUid: "4.5.6");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.NotNull(captured);
            Assert.Equal("1.2.3", captured.StudyInstanceUid);
            Assert.Equal("4.5.6", captured.SeriesInstanceUid);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DicomWadoInstancesResponse (DicomFile list)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_EmptyDicomFileList_Returns200WithMultipartDicom()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
            Assert.Contains("application/dicom", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_SingleDicomFile_ReturnsSingleMultipartPart()
        {
            // Build a minimal DicomFile (CT Image)
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            // Should contain a boundary marker
            Assert.Contains("----dicom-boundary-", body);
            // Should contain the DICOM content-type header for the part
            Assert.Contains("Content-Type: application/dicom", body);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DicomWadoRawInstancesResponse (raw stream list)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_RawInstancesResponse_Returns200WithMultipartDicom()
        {
            var rawBytes = Encoding.ASCII.GetBytes("DICM fake payload");
            var rawInstance = new DicomWadoRawInstance(new MemoryStream(rawBytes), "1.2.840.10008.1.2.1");
            var response = new DicomWadoRawInstancesResponse(new List<DicomWadoRawInstance> { rawInstance });

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(response));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
            var body = await ReadBodyAsync(context);
            Assert.Contains("transfer-syntax=1.2.840.10008.1.2.1", body);
            Assert.Contains("DICM fake payload", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_RawInstanceNullTransferSyntax_OmitsTransferSyntaxParam()
        {
            var rawInstance = new DicomWadoRawInstance(new MemoryStream(new byte[] { 0x01, 0x02 }), transferSyntaxUid: null);
            var response = new DicomWadoRawInstancesResponse(new List<DicomWadoRawInstance> { rawInstance });

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(response));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            // No transfer-syntax parameter when UID is null
            Assert.DoesNotContain("transfer-syntax=", body);
            Assert.Contains("Content-Type: application/dicom", body);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DicomWadoAsyncInstancesResponse (async enumerable)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_AsyncInstancesResponse_Returns200()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoAsyncInstancesResponse(SingleItemAsync(dicomFile))));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_AsyncRawInstancesResponse_Returns200()
        {
            var rawInstance = new DicomWadoRawInstance(
                new MemoryStream(Encoding.ASCII.GetBytes("raw")), "1.2.840.10008.1.2.1");

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoAsyncRawInstancesResponse(SingleItemAsync(rawInstance))));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Metadata — JSON format
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_EmptyDatasets_Returns200WithJsonArray()
        {
            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.StartsWith("application/dicom+json", context.Response.ContentType);
            var body = await ReadBodyAsync(context);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_WithDatasets_Returns200WithNonEmptyJsonArray()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.PatientID, "P001");

            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(
                    new DicomWadoMetadataResponse(new List<DicomDataset> { dataset })));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Metadata — XML format
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_XmlAcceptHeader_Returns200WithMultipartXml()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());

            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(
                    new DicomWadoMetadataResponse(new List<DicomDataset> { dataset })));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: "multipart/related; type=\"application/dicom+xml\"");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("application/dicom+xml", context.Response.ContentType);
            var body = await ReadBodyAsync(context);
            Assert.Contains("NativeDicomModel", body);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Metadata — async enumerable
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_AsyncMetadataResponse_Returns200WithJson()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.PatientID, "A001");

            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(
                    new DicomWadoAsyncMetadataResponse(SingleItemAsync(dataset))));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.StartsWith("application/dicom+json", context.Response.ContentType);
            var body = await ReadBodyAsync(context);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Content negotiation — 406 Not Acceptable
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_UnsupportedAcceptHeader_Returns406()
        {
            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(
                    new DicomWadoMetadataResponse(new List<DicomDataset>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: "text/html");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Failure response HTTP status mapping
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_NotFoundResponse_Returns404()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebNotFoundResponse("study not found")));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Equal("study not found", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_ForbiddenResponse_Returns403()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebForbiddenResponse()));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_UnauthorizedResponse_Returns401()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebUnauthorizedResponse()));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_BadRequestResponse_Returns400()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebBadRequestResponse("invalid uid")));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Equal("invalid uid", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_UnavailableResponse_Returns503()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebUnavailableResponse("temporarily down")));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_NotFoundResponse_Returns404()
        {
            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWebNotFoundResponse()));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Provider exception → 503
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_ProviderThrows_Returns503()
        {
            var service = new TestWadoService((req, ct) =>
                throw new InvalidOperationException("storage unavailable"));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadataRequest_ProviderThrows_Returns503()
        {
            var service = new TestWadoService(
                instancesHandler: (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())),
                metadataHandler: (req, ct) => throw new InvalidOperationException("db down"));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // WADO + QIDO coexistence
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A service that implements both QIDO and WADO providers.
        /// </summary>
        private class WadoAndQidoService : DicomWebService, IDicomQidoProvider, IDicomWadoProvider
        {
            public Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
        }

        [FactForNetCore]
        public async Task WadoAndQidoService_QidoRequestRouted_Returns200()
        {
            var service = new WadoAndQidoService();
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task WadoAndQidoService_WadoInstancesRequestRouted_Returns200()
        {
            var service = new WadoAndQidoService();
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");

            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task WadoAndQidoService_WadoMetadataRequestRouted_Returns200()
        {
            var service = new WadoAndQidoService();
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");

            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DicomWebNotFoundResponse (also usable from QIDO, tested here for completeness)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task QidoProvider_ReturnsNotFoundResponse_Returns404()
        {
            // NotFoundResponse implements IDicomQidoResponse (via DicomWebFailureResponse)
            // so QIDO providers can return it; verify QidoResponseWriter maps it to 404.
            var service = new DicomWebServiceTests_QidoNotFoundService();
            var context = BuildHttpContext();

            await service.HandleQidoStudiesRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        private class DicomWebServiceTests_QidoNotFoundService : DicomWebService, IDicomQidoProvider
        {
            public Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomQidoResponse>(new DicomWebNotFoundResponse("not found"));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

#pragma warning disable CS1998
        private static async IAsyncEnumerable<T> SingleItemAsync<T>(T item)
        {
            yield return item;
        }
#pragma warning restore CS1998
    }
}

#endif
