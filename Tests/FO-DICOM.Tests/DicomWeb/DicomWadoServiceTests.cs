// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore;
using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        // Transfer-syntax content negotiation
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_NoAcceptHeader_Returns200WithDefaultBehavior()
        {
            // Missing Accept → pragmatic default (Explicit VR LE), not 406.
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3"); // no acceptHeader
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_WildcardAccept_Returns200()
        {
            // */* → accept any, treated as default
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", acceptHeader: "*/*");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_ApplicationDicomAccept_Returns200()
        {
            // application/dicom with no transfer-syntax param → default (Explicit VR LE)
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: "multipart/related; type=\"application/dicom\"");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_UnsupportedAcceptType_Returns406()
        {
            // text/html is not application/dicom → 406
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", acceptHeader: "text/html");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_UnsupportedAcceptType_ProviderNotCalled()
        {
            // 406 should be returned before the provider is ever invoked
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3", acceptHeader: "image/jpeg");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_TransferSyntaxWildcard_SetsAcceptsAnyOnRequest()
        {
            // transfer-syntax=* → AcceptsAnyTransferSyntax=true, RequestedTransferSyntax=null
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: "multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(captured);
            Assert.True(captured.AcceptsAnyTransferSyntax);
            Assert.Null(captured.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_SpecificTransferSyntax_SetsRequestedSyntaxOnRequest()
        {
            // transfer-syntax=1.2.840.10008.1.2.1 (Explicit VR LE) → RequestedTransferSyntax set
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            const string explicitVrLe = "1.2.840.10008.1.2.1";
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: $"multipart/related; type=\"application/dicom\"; transfer-syntax={explicitVrLe}");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(captured);
            Assert.False(captured.AcceptsAnyTransferSyntax);
            Assert.NotNull(captured.RequestedTransferSyntax);
            Assert.Equal(explicitVrLe, captured.RequestedTransferSyntax!.UID.UID);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_NoAcceptHeader_RequestHasDefaultSyntax()
        {
            // No Accept → default Explicit VR LE is applied; AcceptsAny is false
            DicomWadoRequest captured = null;
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.False(captured.AcceptsAnyTransferSyntax);
            Assert.NotNull(captured.RequestedTransferSyntax);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, captured.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_TransferSyntaxWildcard_DicomFileNotTranscoded()
        {
            // transfer-syntax=* → file should be returned in its original syntax
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            // Default internal syntax is Explicit VR LE already, so we can verify the
            // transfer-syntax param in the part header matches the file's own syntax.
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: "multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            // transfer-syntax should appear in the Content-Type of the part
            Assert.Contains("transfer-syntax=", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_SpecificTransferSyntax_PartContentTypeIncludesSyntax()
        {
            // Explicit VR LE requested, file already in Explicit VR LE → no transcoding needed,
            // but transfer-syntax should appear in the per-part Content-Type header.
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            const string explicitVrLe = "1.2.840.10008.1.2.1";
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: $"multipart/related; type=\"application/dicom\"; transfer-syntax={explicitVrLe}");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains($"transfer-syntax={explicitVrLe}", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_RawResponse_TransferSyntaxRequestIgnored_Returns200()
        {
            // Raw responses pass through as-is regardless of requested transfer syntax.
            const string explicitVrLe = "1.2.840.10008.1.2.1";
            var rawInstance = new DicomWadoRawInstance(
                new MemoryStream(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
                transferSyntaxUid: explicitVrLe);
            var response = new DicomWadoRawInstancesResponse(new List<DicomWadoRawInstance> { rawInstance });

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(response));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: $"multipart/related; type=\"application/dicom\"; transfer-syntax={explicitVrLe}");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains($"transfer-syntax={explicitVrLe}", body);
        }

        [FactForNetCore]
        public async Task HandleWadoInstancesRequest_TranscodeFailure_Returns406()
        {
            // Request a transfer syntax that requires a codec that won't be available
            // in this minimal test environment. Use a known-compressed UID that fo-dicom
            // cannot encode without a native codec plugin (e.g., JPEG 2000 Lossless).
            const string jpeg2kLossless = "1.2.840.10008.1.2.4.90";

            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.Rows, (ushort)2);
            dataset.Add(DicomTag.Columns, (ushort)2);
            dataset.Add(DicomTag.BitsAllocated, (ushort)8);
            dataset.Add(DicomTag.BitsStored, (ushort)8);
            dataset.Add(DicomTag.HighBit, (ushort)7);
            dataset.Add(DicomTag.PixelRepresentation, (ushort)0);
            dataset.Add(DicomTag.SamplesPerPixel, (ushort)1);
            dataset.Add(DicomTag.PhotometricInterpretation, "MONOCHROME2");
            dataset.Add(DicomTag.PixelData, new byte[] { 0, 0, 0, 0 });
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3",
                acceptHeader: $"multipart/related; type=\"application/dicom\"; transfer-syntax={jpeg2kLossless}");
            await service.HandleWadoInstancesRequestAsync(context);

            // Without a JPEG-2000 codec registered, transcoding should fail → 406
            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // NegotiateInstanceFormat unit tests (parser level)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public void NegotiateInstanceFormat_NoAcceptHeader_ReturnsDefaultExplicitVrLe()
        {
            var context = BuildHttpContext(); // no Accept header
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.False(result.AcceptsAnyTransferSyntax);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_WildcardAccept_ReturnsDefaultExplicitVrLe()
        {
            var context = BuildHttpContext("*/*");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.False(result.AcceptsAnyTransferSyntax);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_ApplicationDicomNoTs_ReturnsDefaultExplicitVrLe()
        {
            var context = BuildHttpContext("multipart/related; type=\"application/dicom\"");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.False(result.AcceptsAnyTransferSyntax);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_TransferSyntaxWildcard_ReturnsAcceptsAny()
        {
            var context = BuildHttpContext("multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.True(result.AcceptsAnyTransferSyntax);
            Assert.Null(result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_ExplicitVrLeUid_ReturnsExplicitVrLe()
        {
            var context = BuildHttpContext(
                "multipart/related; type=\"application/dicom\"; transfer-syntax=1.2.840.10008.1.2.1");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.False(result.AcceptsAnyTransferSyntax);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_Jpeg2000LosslessUid_ReturnsJpeg2000Lossless()
        {
            const string jpeg2kLossless = "1.2.840.10008.1.2.4.90";
            var context = BuildHttpContext(
                $"multipart/related; type=\"application/dicom\"; transfer-syntax={jpeg2kLossless}");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.False(result.AcceptsAnyTransferSyntax);
            Assert.NotNull(result.RequestedTransferSyntax);
            Assert.Equal(jpeg2kLossless, result.RequestedTransferSyntax!.UID.UID);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_UnsupportedMediaType_ReturnsNotAcceptable()
        {
            var context = BuildHttpContext("text/html");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.False(result.IsAcceptable);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_ImageJpegMediaType_ReturnsNotAcceptable()
        {
            var context = BuildHttpContext("image/jpeg");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.False(result.IsAcceptable);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_TransferSyntaxInQuotes_ParsedCorrectly()
        {
            var context = BuildHttpContext(
                "multipart/related; type=\"application/dicom\"; transfer-syntax=\"1.2.840.10008.1.2.1\"");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal(DicomTransferSyntax.ExplicitVRLittleEndian, result.RequestedTransferSyntax);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_ApplicationDicomPlusJson_ReturnsNotAcceptable()
        {
            // application/dicom+json is NOT application/dicom — must not be treated as a match.
            // Previously the substring "application/dicom" would be found without a word-boundary
            // check and the parser would incorrectly return a result instead of 406.
            var context = BuildHttpContext("application/dicom+json");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.False(result.IsAcceptable);
        }

        [FactForNetCore]
        public void NegotiateInstanceFormat_DicomPlusJsonAndDicom_DicomIsFound()
        {
            // When both application/dicom+json and application/dicom appear in the Accept header,
            // the parser must skip the +json variant and find the bare application/dicom token.
            var context = BuildHttpContext(
                "application/dicom+json, multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            var result = WadoResponseWriter.NegotiateInstanceFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.True(result.AcceptsAnyTransferSyntax);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Content-Location per multipart part (PS3.18 Section 10.4.1.1)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A TestWadoService whose ContentLocationMode can be set at construction time.
        /// </summary>
        private class TestWadoServiceWithMode : TestWadoService
        {
            private readonly ContentLocationMode _mode;

            public TestWadoServiceWithMode(
                ContentLocationMode mode,
                Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> instancesHandler)
                : base(instancesHandler)
            {
                _mode = mode;
            }

            protected override ContentLocationMode ContentLocationMode => _mode;
        }

        /// <summary>
        /// Sets up an endpoint on the <see cref="DefaultHttpContext"/> so that
        /// <c>context.GetEndpoint()?.Metadata.GetMetadata&lt;DicomWebEndpointMetadata&gt;()</c>
        /// returns the metadata carrying <paramref name="urlPrefix"/>.
        /// </summary>
        private static DefaultHttpContext BuildHttpContextWithEndpointMetadata(
            string urlPrefix,
            string studyUid = null,
            string seriesUid = null,
            string sopUid = null,
            string acceptHeader = null)
        {
            var context = BuildHttpContextWithRouteValues(studyUid, seriesUid, sopUid, acceptHeader);
            var metadata = new DicomWebEndpointMetadata(urlPrefix);
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
            return context;
        }

        [FactForNetCore]
        public async Task ContentLocation_DicomFile_WithAllUids_RelativeHeaderPresent()
        {
            // A DicomFile with all three UIDs + endpoint metadata → Content-Location relative path
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains("Content-Location: /dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_DicomFile_MissingUids_HeaderOmitted()
        {
            // DicomFile with SOPClassUID + SOPInstanceUID but missing StudyInstanceUID and
            // SeriesInstanceUID → GetSingleValueOrDefault returns "" for those → Content-Location
            // must be omitted gracefully (all three UIDs are required to build the path).
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            // No StudyInstanceUID, no SeriesInstanceUID
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.DoesNotContain("Content-Location:", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_DicomFile_NoEndpointMetadata_HeaderOmitted()
        {
            // No endpoint metadata (unit test default context) → urlPrefix is null → no header
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            // No endpoint metadata — standard context without SetEndpoint
            var context = BuildHttpContextWithRouteValues(studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.DoesNotContain("Content-Location:", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_RawInstance_WithAllUids_HeaderPresent()
        {
            // DicomWadoRawInstance constructed with UIDs → Content-Location header appears
            var rawBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            var rawInstance = new DicomWadoRawInstance(
                new MemoryStream(rawBytes),
                "1.2.840.10008.1.2.1",
                "1.2.3", "4.5.6", "7.8.9");

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoRawInstancesResponse(new List<DicomWadoRawInstance> { rawInstance })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains("Content-Location: /dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_RawInstance_WithoutUids_HeaderOmitted()
        {
            // DicomWadoRawInstance constructed without UIDs → Content-Location must be omitted
            var rawInstance = new DicomWadoRawInstance(
                new MemoryStream(new byte[] { 0x01, 0x02 }),
                "1.2.840.10008.1.2.1"); // 2-arg ctor — no UIDs

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoRawInstancesResponse(new List<DicomWadoRawInstance> { rawInstance })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.DoesNotContain("Content-Location:", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_RelativePathFormat_StartsWithSlashAndContainsAllUids()
        {
            // Verify exact relative path format
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.840.99.1");
            dataset.Add(DicomTag.SeriesInstanceUID, "1.2.840.99.2");
            dataset.Add(DicomTag.SOPInstanceUID, "1.2.840.99.3");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/wado", studyUid: "1.2.840.99.1");
            await service.HandleWadoInstancesRequestAsync(context);

            var body = await ReadBodyAsync(context);
            // Path must start with the prefix, and contain all three UID segments
            Assert.Contains("Content-Location: /wado/studies/1.2.840.99.1/series/1.2.840.99.2/instances/1.2.840.99.3", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_ModeNone_HeaderNotEmitted()
        {
            // ContentLocationMode.None → no Content-Location header regardless of UIDs/prefix
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoServiceWithMode(
                ContentLocationMode.None,
                (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.DoesNotContain("Content-Location:", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_ModeAbsolute_HeaderContainsSchemeAndHost()
        {
            // ContentLocationMode.Absolute → full URL with scheme://host prefix
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoServiceWithMode(
                ContentLocationMode.Absolute,
                (req, ct) => Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            // DefaultHttpContext defaults: Scheme = "http", Host = ""
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("pacs.example.com");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains(
                "Content-Location: https://pacs.example.com/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9",
                body);
        }

        [FactForNetCore]
        public async Task ContentLocation_AsyncDicomFile_WithAllUids_HeaderPresentPerPart()
        {
            // Async streaming DicomFile → Content-Location per part
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoAsyncInstancesResponse(SingleItemAsync(dicomFile))));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains("Content-Location: /dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9", body);
        }

        [FactForNetCore]
        public async Task ContentLocation_ContentLocationAfterContentType_BeforeBlankLine()
        {
            // Content-Location must appear between Content-Type and the blank separator line
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID, "7.8.9");
            var dicomFile = new DicomFile(dataset);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContextWithEndpointMetadata("/dicomweb", studyUid: "1.2.3");
            await service.HandleWadoInstancesRequestAsync(context);

            var body = await ReadBodyAsync(context);
            // Content-Type should appear before Content-Location in the body text
            var ctIdx = body.IndexOf("Content-Type: application/dicom", StringComparison.Ordinal);
            var clIdx = body.IndexOf("Content-Location:", StringComparison.Ordinal);
            Assert.True(ctIdx >= 0, "Content-Type not found");
            Assert.True(clIdx >= 0, "Content-Location not found");
            Assert.True(ctIdx < clIdx, "Content-Type should appear before Content-Location");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // BulkDataURI in metadata responses (PS3.18 Section 10.4.1.1.2)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoMetadata_DatasetWithPixelData_JsonContainsBulkDataUri()
        {
            // Dataset with OW pixel data — the framework must replace it with BulkDataURI.
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID,      DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID,  "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID,    "7.8.9");
            dataset.Add(new DicomOtherWord(DicomTag.PixelData,
                new FellowOakDicom.IO.Buffer.MemoryByteBuffer(new byte[] { 0x01, 0x02, 0x03, 0x04 })));

            var service = new TestWadoService(
                instancesHandler: null,
                metadataHandler: (req, ct) =>
                    Task.FromResult<IDicomWadoMetadataResponse>(
                        new DicomWadoMetadataResponse(new List<DicomDataset> { dataset })));

            var context = BuildHttpContextWithEndpointMetadata(
                "/dicomweb",
                studyUid:  "1.2.3",
                seriesUid: "4.5.6",
                sopUid:    "7.8.9",
                acceptHeader: "application/dicom+json");

            await service.HandleWadoMetadataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);
            Assert.Contains("BulkDataURI", body);
            Assert.DoesNotContain("InlineBinary", body);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadata_DatasetWithPixelData_BulkUriContainsInstancePath()
        {
            // The BulkDataURI must point to the instance + /bulk/7FE00010.
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID,      DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID,  "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID,    "7.8.9");
            dataset.Add(new DicomOtherWord(DicomTag.PixelData,
                new FellowOakDicom.IO.Buffer.MemoryByteBuffer(new byte[] { 0xAB, 0xCD })));

            var service = new TestWadoService(
                instancesHandler: null,
                metadataHandler: (req, ct) =>
                    Task.FromResult<IDicomWadoMetadataResponse>(
                        new DicomWadoMetadataResponse(new List<DicomDataset> { dataset })));

            var context = BuildHttpContextWithEndpointMetadata(
                "/dicomweb",
                studyUid:  "1.2.3",
                seriesUid: "4.5.6",
                sopUid:    "7.8.9",
                acceptHeader: "application/dicom+json");

            await service.HandleWadoMetadataRequestAsync(context);

            var body = await ReadBodyAsync(context);
            Assert.Contains("/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9/bulk/7FE00010", body);
        }

        [FactForNetCore]
        public async Task HandleWadoMetadata_DatasetWithoutPixelData_NoBulkDataUri()
        {
            // Dataset with only non-bulk VRs — JSON must not contain BulkDataURI.
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID,       DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID,  "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID,    "7.8.9");
            dataset.Add(DicomTag.PatientName,       "Doe^John");

            var service = new TestWadoService(
                instancesHandler: null,
                metadataHandler: (req, ct) =>
                    Task.FromResult<IDicomWadoMetadataResponse>(
                        new DicomWadoMetadataResponse(new List<DicomDataset> { dataset })));

            var context = BuildHttpContextWithEndpointMetadata(
                "/dicomweb",
                studyUid:  "1.2.3",
                seriesUid: "4.5.6",
                sopUid:    "7.8.9",
                acceptHeader: "application/dicom+json");

            await service.HandleWadoMetadataRequestAsync(context);

            var body = await ReadBodyAsync(context);
            Assert.DoesNotContain("BulkDataURI", body);
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
