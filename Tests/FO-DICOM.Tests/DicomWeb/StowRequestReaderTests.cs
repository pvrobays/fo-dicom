// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore.DicomWebService;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class StowRequestReaderTests
    {
        // ─── Helpers ──────────────────────────────────────────────────────────────

        private const string Boundary = "dicom-boundary-test";

        /// <summary>
        /// Builds a multipart/related request body stream with the given parts.
        /// Each part is (contentType, body bytes).
        /// </summary>
        private static Stream BuildMultipartBody(params (string contentType, byte[] body)[] parts)
        {
            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
            {
                foreach (var (contentType, body) in parts)
                {
                    writer.Write($"--{Boundary}\r\n");
                    writer.Write($"Content-Type: {contentType}\r\n");
                    writer.Write("\r\n");
                    writer.Flush();
                    ms.Write(body, 0, body.Length);
                    writer.Write("\r\n");
                }
                writer.Write($"--{Boundary}--\r\n");
                writer.Flush();
            }
            ms.Position = 0;
            return ms;
        }

        private static byte[] BuildDicomBytes(string sopClassUid = null, string sopInstanceUid = null)
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.SOPClassUID, sopClassUid ?? DicomUID.CTImageStorage.UID);
            dataset.Add(DicomTag.SOPInstanceUID, sopInstanceUid ?? DicomUID.Generate().UID);
            dataset.Add(DicomTag.PatientName, "Test^Patient");
            var file = new DicomFile(dataset);
            var ms = new MemoryStream();
            file.Save(ms);
            return ms.ToArray();
        }

        private static DefaultHttpContext BuildRequest(Stream body, string contentType)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = contentType;
            context.Request.Body = body;
            return context;
        }

        // ─── GetBoundary tests ────────────────────────────────────────────────

        [FactForNetCore]
        public void GetBoundary_ValidMultipartRelated_ReturnsBoundary()
        {
            var context = BuildRequest(Stream.Null,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var boundary = StowRequestReader.GetBoundary(context.Request);
            Assert.Equal(Boundary, boundary);
        }

        [FactForNetCore]
        public void GetBoundary_MissingContentType_ReturnsNull()
        {
            var context = new DefaultHttpContext();
            var boundary = StowRequestReader.GetBoundary(context.Request);
            Assert.Null(boundary);
        }

        [FactForNetCore]
        public void GetBoundary_NotMultipart_ReturnsNull()
        {
            var context = BuildRequest(Stream.Null, "application/dicom");
            var boundary = StowRequestReader.GetBoundary(context.Request);
            Assert.Null(boundary);
        }

        [FactForNetCore]
        public void GetBoundary_MultipartRelatedNoBoundary_ReturnsNull()
        {
            var context = BuildRequest(Stream.Null, "multipart/related; type=\"application/dicom\"");
            var boundary = StowRequestReader.GetBoundary(context.Request);
            Assert.Null(boundary);
        }

        // ─── ReadAllAsync tests ───────────────────────────────────────────────

        [FactForNetCore]
        public async Task ReadAllAsync_MissingBoundary_ReturnsFailure()
        {
            var context = BuildRequest(new MemoryStream(), "application/dicom");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorReason);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_EmptyBody_ReturnsFailure()
        {
            // No parts — multipart body with just the closing boundary
            var ms = new MemoryStream();
            using (var w = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
            {
                w.Write($"--{Boundary}--\r\n");
                w.Flush();
            }
            ms.Position = 0;
            var context = BuildRequest(ms,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Contains("no DICOM parts", result.ErrorReason, StringComparison.OrdinalIgnoreCase);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_NonDicomContentType_ReturnsFailure()
        {
            var body = BuildMultipartBody(("application/octet-stream", new byte[] { 1, 2, 3 }));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Contains("unsupported Content-Type", result.ErrorReason, StringComparison.OrdinalIgnoreCase);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_CorruptDicomData_ReturnsFailure()
        {
            var body = BuildMultipartBody(("application/dicom", new byte[] { 0xFF, 0xFF, 0x00, 0x00 }));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Contains("could not be parsed", result.ErrorReason, StringComparison.OrdinalIgnoreCase);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_SingleValidDicomPart_ReturnsOneFile()
        {
            var dicomBytes = BuildDicomBytes();
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Files);
            Assert.Single(result.Files);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_MultipleValidDicomParts_ReturnsAllFiles()
        {
            var bytes1 = BuildDicomBytes(sopInstanceUid: "1.2.3.1");
            var bytes2 = BuildDicomBytes(sopInstanceUid: "1.2.3.2");
            var bytes3 = BuildDicomBytes(sopInstanceUid: "1.2.3.3");
            var body = BuildMultipartBody(
                ("application/dicom", bytes1),
                ("application/dicom", bytes2),
                ("application/dicom", bytes3));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Files.Count);
        }

        [FactForNetCore]
        public async Task ReadAllAsync_ApplicationDicomWithTransferSyntaxParam_IsAccepted()
        {
            // Content-Type: application/dicom; transfer-syntax=1.2.840.10008.1.2.1 should be accepted
            var dicomBytes = BuildDicomBytes();
            var body = BuildMultipartBody(
                ("application/dicom; transfer-syntax=1.2.840.10008.1.2.1", dicomBytes));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");
            var result = await StowRequestReader.ReadAllAsync(context.Request, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Single(result.Files);
        }

        // ─── StreamAsync tests ────────────────────────────────────────────────

        [FactForNetCore]
        public async Task StreamAsync_MissingBoundary_ThrowsStowStreamError()
        {
            var context = BuildRequest(new MemoryStream(), "application/dicom");
            await Assert.ThrowsAsync<StowStreamError>(async () =>
            {
                await foreach (var _ in StowRequestReader.StreamAsync(
                    context.Request, CancellationToken.None)) { }
            });
        }

        [FactForNetCore]
        public async Task StreamAsync_SingleValidPart_YieldsOneFile()
        {
            var dicomBytes = BuildDicomBytes();
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");

            int count = 0;
            await foreach (var file in StowRequestReader.StreamAsync(
                context.Request, CancellationToken.None))
            {
                count++;
                Assert.NotNull(file);
            }
            Assert.Equal(1, count);
        }

        [FactForNetCore]
        public async Task StreamAsync_MultipleValidParts_YieldsAllFiles()
        {
            var bytes1 = BuildDicomBytes(sopInstanceUid: "1.2.3.1");
            var bytes2 = BuildDicomBytes(sopInstanceUid: "1.2.3.2");
            var body = BuildMultipartBody(
                ("application/dicom", bytes1),
                ("application/dicom", bytes2));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");

            int count = 0;
            await foreach (var file in StowRequestReader.StreamAsync(
                context.Request, CancellationToken.None))
            {
                count++;
            }
            Assert.Equal(2, count);
        }

        [FactForNetCore]
        public async Task StreamAsync_NonDicomPart_ThrowsStowStreamError()
        {
            var body = BuildMultipartBody(("text/plain", new byte[] { 0x41, 0x42 }));
            var context = BuildRequest(body,
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}");

            await Assert.ThrowsAsync<StowStreamError>(async () =>
            {
                await foreach (var _ in StowRequestReader.StreamAsync(
                    context.Request, CancellationToken.None)) { }
            });
        }
    }
}

#endif
