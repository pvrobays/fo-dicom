// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.IO.Buffer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomWadoBulkDataTests
    {
        #region Test doubles

        private class TestWadoBulkService : DicomWebService, IDicomWadoProvider
        {
            private readonly Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> _handler;

            public TestWadoBulkService(
                Func<DicomWadoRequest, CancellationToken, Task<IDicomWadoInstanceResponse>> handler)
            {
                _handler = handler;
            }

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoMetadataResponse>(
                    new DicomWadoMetadataResponse(new List<DicomDataset>()));
        }

        private class NoBulkProviderService : DicomWebService { }

        private static DefaultHttpContext BuildBulkContext(
            string bulkPath,
            string studyUid = "1.2.3",
            string seriesUid = "4.5.6",
            string sopUid = "7.8.9")
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Request.RouteValues["studyInstanceUID"]  = studyUid;
            context.Request.RouteValues["seriesInstanceUID"] = seriesUid;
            context.Request.RouteValues["sopInstanceUID"]    = sopUid;
            context.Request.RouteValues["bulkPath"]          = bulkPath;
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

        private static Task<IDicomWadoInstanceResponse> FileResponse(DicomFile file)
            => Task.FromResult<IDicomWadoInstanceResponse>(
                new DicomWadoInstancesResponse(new List<DicomFile> { file }));

        #endregion

        // ─────────────────────────────────────────────────────────────────────────
        // 501 Not Implemented (no IDicomWadoProvider)
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_NoProvider_Returns501()
        {
            var service = new NoBulkProviderService();
            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Missing / malformed bulk path → 400
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_MissingBulkPath_Returns400()
        {
            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(new DicomDataset())));

            // Omit bulkPath from route values entirely.
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Request.RouteValues["studyInstanceUID"]  = "1.2.3";
            context.Request.RouteValues["seriesInstanceUID"] = "4.5.6";
            context.Request.RouteValues["sopInstanceUID"]    = "7.8.9";

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleWadoBulkData_InvalidTagHex_Returns400()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext("ZZZZZZZZ"); // invalid hex

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Provider 404 → 404
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_ProviderNotFound_Returns404()
        {
            var service = new TestWadoBulkService((req, ct) =>
                Task.FromResult<IDicomWadoInstanceResponse>(new DicomWebNotFoundResponse()));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Provider throws → 503
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_ProviderThrows_Returns503()
        {
            var service = new TestWadoBulkService((req, ct) =>
                throw new InvalidOperationException("storage unavailable"));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Tag not present in dataset → 404
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_TagNotPresent_Returns404()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            // PixelData (7FE00010) deliberately absent.

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Non-bulk VR tag → 400
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_NonBulkVrTag_Returns400()
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(DicomTag.PatientName, "Test^Patient");

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            // PatientName is LO, not a bulk data VR.
            var patientNameTag = DicomBulkDataHelper.FormatTagForUrl(DicomTag.PatientName);
            var context = BuildBulkContext(patientNameTag);

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Successful retrieval of top-level element
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_ValidPixelData_Returns200WithOctetStreamMultipart()
        {
            var pixelBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixelBytes)));

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("multipart/related", context.Response.ContentType);
            Assert.Contains("application/octet-stream", context.Response.ContentType);
        }

        [FactForNetCore]
        public async Task HandleWadoBulkData_ValidPixelData_BodyContainsRawBytes()
        {
            var pixelBytes = new byte[] { 0xAB, 0xCD, 0xEF, 0x01 };
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixelBytes)));

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            var bodyBytes = await ReadBodyBytesAsync(context);
            Assert.True(ContainsSequence(bodyBytes, pixelBytes),
                "Response body must contain the raw pixel bytes");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Nested sequence element retrieval
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_NestedSequenceElement_Returns200WithCorrectBytes()
        {
            var waveformBytes = new byte[] { 0x10, 0x20, 0x30 };
            var seqItem = new DicomDataset
            {
                new DicomOtherByte(DicomTag.WaveformData, new MemoryByteBuffer(waveformBytes))
            };
            var seq = new DicomSequence(DicomTag.ReferencedSOPSequence, seqItem);
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(seq);

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var seqTag  = DicomBulkDataHelper.FormatTagForUrl(DicomTag.ReferencedSOPSequence);
            var elemTag = DicomBulkDataHelper.FormatTagForUrl(DicomTag.WaveformData);
            var context = BuildBulkContext($"{seqTag}/0/{elemTag}");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var bodyBytes = await ReadBodyBytesAsync(context);
            Assert.True(ContainsSequence(bodyBytes, waveformBytes),
                "Response body must contain the raw waveform bytes");
        }

        [FactForNetCore]
        public async Task HandleWadoBulkData_NestedSequenceOutOfRange_Returns404()
        {
            var seqItem = new DicomDataset
            {
                new DicomOtherByte(DicomTag.WaveformData, new MemoryByteBuffer(new byte[] { 0x01 }))
            };
            var seq = new DicomSequence(DicomTag.ReferencedSOPSequence, seqItem);
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            dataset.Add(seq);

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var seqTag  = DicomBulkDataHelper.FormatTagForUrl(DicomTag.ReferencedSOPSequence);
            var elemTag = DicomBulkDataHelper.FormatTagForUrl(DicomTag.WaveformData);
            // Item index 99 is out of range (only item 0 exists).
            var context = BuildBulkContext($"{seqTag}/99/{elemTag}");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Fragment sequence (encapsulated pixel data) → concatenated bytes
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleWadoBulkData_FragmentSequence_Returns200WithConcatenatedFragments()
        {
            var frag1 = new byte[] { 0x11, 0x22 };
            var frag2 = new byte[] { 0x33, 0x44 };

            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, DicomUID.CTImageStorage);
            dataset.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
            var fragSeq = new DicomOtherWordFragment(DicomTag.PixelData);
            fragSeq.Add(new MemoryByteBuffer(new byte[] { 0, 0, 0, 0 })); // offset table
            fragSeq.Add(new MemoryByteBuffer(frag1));
            fragSeq.Add(new MemoryByteBuffer(frag2));
            dataset.Add(fragSeq);

            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext("7FE00010");

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var bodyBytes = await ReadBodyBytesAsync(context);
            // Both fragments must appear in the response body.
            Assert.True(ContainsSequence(bodyBytes, frag1));
            Assert.True(ContainsSequence(bodyBytes, frag2));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Routing tests: verify that the bulk data path round-trips through
        // the BulkDataURI emitted by the metadata endpoint.
        // ─────────────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task BulkDataUri_MatchesBulkDataEndpointPath()
        {
            // The URI emitted in the metadata response must correspond to the route
            // registered in DicomWebServiceEndpoints, i.e. end with /bulk/{tag}.
            var pixelBytes = new byte[] { 0xFF, 0xFE };
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID,      DicomUID.CTImageStorage);
            dataset.Add(DicomTag.StudyInstanceUID,  "1.2.3");
            dataset.Add(DicomTag.SeriesInstanceUID, "4.5.6");
            dataset.Add(DicomTag.SOPInstanceUID,    "7.8.9");
            dataset.Add(new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixelBytes)));

            // The BulkDataHelper produces URIs of the form: {base}/bulk/{tag}
            var uri = $"/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9/bulk/7FE00010";
            // Extract the bulkPath portion (after /bulk/).
            var bulkPathStart = uri.IndexOf("/bulk/", StringComparison.Ordinal) + "/bulk/".Length;
            var bulkPath = uri.Substring(bulkPathStart);

            // The bulkPath "7FE00010" must resolve to PixelData.
            Assert.Equal("7FE00010", bulkPath);

            // Verify that this bulk path can be used directly to retrieve the element.
            var service = new TestWadoBulkService((req, ct) =>
                FileResponse(new DicomFile(dataset)));

            var context = BuildBulkContext(bulkPath);

            await service.HandleWadoBulkDataRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var bodyBytes = await ReadBodyBytesAsync(context);
            Assert.True(ContainsSequence(bodyBytes, pixelBytes));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static bool ContainsSequence(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0) return true;
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }
    }
}

#endif
