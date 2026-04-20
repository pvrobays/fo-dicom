// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomWadoFrameTests
    {
        #region Test doubles

        /// <summary>
        /// A concrete DicomWebService whose OnRetrieveInstancesAsync is driven by a delegate.
        /// </summary>
        private class TestWadoService : global::FellowOakDicom.AspNetCore.DicomWebService.DicomWebService, IDicomWadoProvider
        {
            private readonly System.Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> _instancesHandler;

            public TestWadoService(
                System.Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> instancesHandler)
            {
                _instancesHandler = instancesHandler;
            }

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _instancesHandler(request, cancellationToken);

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
        }

        /// <summary>
        /// A DicomWebService that does NOT implement IDicomWadoProvider.
        /// </summary>
        private class WadoNotImplementedService : global::FellowOakDicom.AspNetCore.DicomWebService.DicomWebService
        {
        }

        private static DefaultHttpContext BuildHttpContext(
            string studyUid = "1.2.3",
            string seriesUid = "4.5.6",
            string sopUid = "7.8.9",
            string frameList = "1",
            string acceptHeader = null)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            if (studyUid != null)  context.Request.RouteValues["studyInstanceUID"]  = studyUid;
            if (seriesUid != null) context.Request.RouteValues["seriesInstanceUID"] = seriesUid;
            if (sopUid != null)    context.Request.RouteValues["sopInstanceUID"]    = sopUid;
            if (frameList != null) context.Request.RouteValues["frameList"]         = frameList;
            if (acceptHeader != null)
            {
                context.Request.Headers["Accept"] = acceptHeader;
            }
            return context;
        }

        private static async Task<string> ReadBodyAsync(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new System.IO.StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        /// <summary>
        /// Builds a minimal single-frame uncompressed DICOM file with the given pixel bytes.
        /// </summary>
        private static DicomFile BuildUncompressedDicomFile(byte[] framePixels, int width = 2, int height = 2)
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.Rows,            (ushort)height);
            dataset.Add(DicomTag.Columns,         (ushort)width);
            dataset.Add(DicomTag.BitsAllocated,   (ushort)8);
            dataset.Add(DicomTag.BitsStored,      (ushort)8);
            dataset.Add(DicomTag.HighBit,         (ushort)7);
            dataset.Add(DicomTag.PixelRepresentation, (ushort)0);
            dataset.Add(DicomTag.SamplesPerPixel, (ushort)1);

            var pixelData = DicomPixelData.Create(dataset, newPixelData: true);
            pixelData.AddFrame(new MemoryByteBuffer(framePixels));

            return new DicomFile(dataset);
        }

        /// <summary>
        /// Builds a minimal two-frame uncompressed DICOM file.
        /// </summary>
        private static DicomFile BuildTwoFrameUncompressedDicomFile()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.StudyInstanceUID, "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.Rows,            (ushort)2);
            dataset.Add(DicomTag.Columns,         (ushort)2);
            dataset.Add(DicomTag.BitsAllocated,   (ushort)8);
            dataset.Add(DicomTag.BitsStored,      (ushort)8);
            dataset.Add(DicomTag.HighBit,         (ushort)7);
            dataset.Add(DicomTag.PixelRepresentation, (ushort)0);
            dataset.Add(DicomTag.SamplesPerPixel, (ushort)1);

            var pixelData = DicomPixelData.Create(dataset, newPixelData: true);
            pixelData.AddFrame(new MemoryByteBuffer(new byte[] { 0x01, 0x02, 0x03, 0x04 }));
            pixelData.AddFrame(new MemoryByteBuffer(new byte[] { 0x05, 0x06, 0x07, 0x08 }));

            return new DicomFile(dataset);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────────
        // 501 Not Implemented (no IDicomWadoProvider)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_NoWadoProvider_Returns501()
        {
            var service = new WadoNotImplementedService();
            var context = BuildHttpContext();

            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Accept header negotiation — 406 before provider is called
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_UnacceptableMediaType_Returns406BeforeCallingProvider()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(acceptHeader: "text/html");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
            Assert.False(providerCalled, "Provider should not be called when Accept is not acceptable");
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_AcceptApplicationDicom_Returns406()
        {
            // application/dicom is for instance retrieval, not frame retrieval
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(acceptHeader: "multipart/related; type=\"application/dicom\"");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status406NotAcceptable, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Invalid frameList — 400 before provider is called
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_MissingFrameList_Returns400()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            // No frameList route value
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Request.RouteValues["studyInstanceUID"]  = "1.2.3";
            context.Request.RouteValues["seriesInstanceUID"] = "4.5.6";
            context.Request.RouteValues["sopInstanceUID"]    = "7.8.9";
            // frameList intentionally omitted

            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_NonNumericFrameList_Returns400()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(frameList: "abc");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_ZeroFrameNumber_Returns400()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(frameList: "0");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_NegativeFrameNumber_Returns400()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(frameList: "-1");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_PartiallyInvalidFrameList_Returns400()
        {
            bool providerCalled = false;
            var service = new TestWadoService((req, ct) =>
            {
                providerCalled = true;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            });

            var context = BuildHttpContext(frameList: "1,x,3");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.False(providerCalled);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Provider failure responses
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_Provider404_Returns404()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebNotFoundResponse("instance not found")));

            var context = BuildHttpContext();
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_ProviderThrows_Returns503()
        {
            var service = new TestWadoService((req, ct) =>
                throw new System.Exception("DB failure"));

            var context = BuildHttpContext();
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // FrameNumbers on DicomWadoRequest
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_SingleFrame_FrameNumbersSetOnRequest()
        {
            DicomWadoRequest captured = null;
            var dicomFile = BuildUncompressedDicomFile(new byte[] { 0x10, 0x20, 0x30, 0x40 });
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile }));
            });

            var context = BuildHttpContext(frameList: "1");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.NotNull(captured.FrameNumbers);
            Assert.Equal(new[] { 1 }, captured.FrameNumbers);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_MultiFrame_FrameNumbersSetOnRequest()
        {
            DicomWadoRequest captured = null;
            var dicomFile = BuildTwoFrameUncompressedDicomFile();
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile }));
            });

            var context = BuildHttpContext(frameList: "1,2");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.NotNull(captured.FrameNumbers);
            Assert.Equal(new[] { 1, 2 }, captured.FrameNumbers);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Successful single-frame retrieval (uncompressed)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_SingleFrame_Returns200MultipartOctetStream()
        {
            var framePixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var dicomFile = BuildUncompressedDicomFile(framePixels);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(frameList: "1");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
            Assert.Contains("application/octet-stream", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_SingleFrame_BodyContainsBoundaryAndContentType()
        {
            var framePixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var dicomFile = BuildUncompressedDicomFile(framePixels);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(frameList: "1");
            await service.HandleWadoFramesRequestAsync(context);

            var body = await ReadBodyAsync(context);
            Assert.Contains("----dicom-boundary-", body);
            Assert.Contains("Content-Type: application/octet-stream", body);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Default Accept behaviour (*/* and missing → octet-stream)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_WildcardAccept_DefaultsToOctetStream()
        {
            var framePixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var dicomFile = BuildUncompressedDicomFile(framePixels);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(frameList: "1", acceptHeader: "*/*");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("application/octet-stream", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_NoAcceptHeader_DefaultsToOctetStream()
        {
            var framePixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var dicomFile = BuildUncompressedDicomFile(framePixels);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            // No Accept header
            var context = BuildHttpContext(frameList: "1", acceptHeader: null);
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("application/octet-stream", context.Response.ContentType);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Explicit octet-stream Accept header
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_ExplicitOctetStreamAccept_Returns200()
        {
            var framePixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var dicomFile = BuildUncompressedDicomFile(framePixels);

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(
                frameList: "1",
                acceptHeader: "multipart/related; type=\"application/octet-stream\"");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("application/octet-stream", context.Response.ContentType);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Multi-frame retrieval
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_TwoFrames_Returns200WithTwoParts()
        {
            var dicomFile = BuildTwoFrameUncompressedDicomFile();

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(frameList: "1,2");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var body = await ReadBodyAsync(context);

            // Each frame produces a boundary marker
            var boundaryCount = CountOccurrences(body, "----dicom-boundary-");
            // 2 opening boundaries + 1 closing boundary = 3 occurrences of the boundary prefix
            Assert.True(boundaryCount >= 2, $"Expected at least 2 boundary markers, got {boundaryCount}");
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_SingleFrameFromTwoFrameInstance_Returns200()
        {
            var dicomFile = BuildTwoFrameUncompressedDicomFile();

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            var context = BuildHttpContext(frameList: "2"); // request only frame 2
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Frame number out of range
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_FrameOutOfRange_Returns400()
        {
            var dicomFile = BuildUncompressedDicomFile(new byte[] { 0x01, 0x02, 0x03, 0x04 });

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            // Instance has 1 frame; request frame 2
            var context = BuildHttpContext(frameList: "2");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_SecondFrameOutOfRange_Returns400()
        {
            var dicomFile = BuildTwoFrameUncompressedDicomFile();

            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile })));

            // Instance has 2 frames; request frame 1 and 3 (3 is out of range)
            var context = BuildHttpContext(frameList: "1,3");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Provider returns empty list → 404
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_ProviderReturnsEmptyList_Returns404()
        {
            var service = new TestWadoService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile>())));

            var context = BuildHttpContext(frameList: "1");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Route UIDs injected correctly
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoFramesRequest_RouteUids_AllThreeUidsInjectedOnRequest()
        {
            DicomWadoRequest captured = null;
            var dicomFile = BuildUncompressedDicomFile(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var service = new TestWadoService((req, ct) =>
            {
                captured = req;
                return Task.FromResult<IDicomWadoInstanceResponse>(
                    new DicomWadoInstancesResponse(new List<DicomFile> { dicomFile }));
            });

            var context = BuildHttpContext(
                studyUid:  "1.2.3",
                seriesUid: "4.5.6",
                sopUid:    "7.8.9",
                frameList: "1");
            await service.HandleWadoFramesRequestAsync(context);

            Assert.NotNull(captured);
            Assert.Equal("1.2.3", captured.StudyInstanceUid);
            Assert.Equal("4.5.6", captured.SeriesInstanceUid);
            Assert.Equal("7.8.9", captured.SopInstanceUid);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // NegotiateFrameFormat unit tests
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public void NegotiateFrameFormat_NoAcceptHeader_ReturnsDefaultOctetStream()
        {
            var context = new DefaultHttpContext();
            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("application/octet-stream", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_WildcardAccept_ReturnsDefaultOctetStream()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "*/*";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("application/octet-stream", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_OctetStreamType_ReturnsOctetStream()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"application/octet-stream\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("application/octet-stream", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ImageJpeg_ReturnsImageJpeg()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"image/jpeg\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("image/jpeg", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ImageJp2_ReturnsImageJp2()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"image/jp2\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("image/jp2", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ImageXJls_ReturnsImageXJls()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"image/x-jls\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("image/x-jls", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ImageJphc_ReturnsImageJphc()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"image/jphc\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("image/jphc", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ImageDicomRle_ReturnsImageDicomRle()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"image/dicom-rle\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.True(result.IsAcceptable);
            Assert.Equal("image/dicom-rle", result.MediaType);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_TextHtml_NotAcceptable()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "text/html";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.False(result.IsAcceptable);
        }

        [FactForNetCore]
        public void NegotiateFrameFormat_ApplicationDicom_NotAcceptable()
        {
            // application/dicom (instance retrieval type) is not a valid frame type
            var context = new DefaultHttpContext();
            context.Request.Headers["Accept"] = "multipart/related; type=\"application/dicom\"";

            var result = WadoResponseWriter.NegotiateFrameFormat(context);

            Assert.False(result.IsAcceptable);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }
    }
}

#endif
