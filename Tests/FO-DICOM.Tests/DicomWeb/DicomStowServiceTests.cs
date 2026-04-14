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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    [Collection(TestCollections.General)]
    public class DicomStowServiceTests
    {
        #region Test doubles

        private class TestStowService : DicomWebService, IDicomStowProvider
        {
            private readonly Func<DicomStowRequest, CancellationToken, Task<IDicomStowResponse>> _handler;

            public TestStowService(
                Func<DicomStowRequest, CancellationToken, Task<IDicomStowResponse>> handler)
            {
                _handler = handler;
            }

            public Task<IDicomStowResponse> OnStoreInstancesAsync(
                DicomStowRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        private class NoStowProviderService : DicomWebService { }

        #endregion

        #region Helpers

        private const string Boundary = "stow-test-boundary";

        private static DefaultHttpContext BuildStowContext(
            Stream body,
            string studyUid = null,
            string acceptHeader = null)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Request.Method = "POST";
            context.Request.ContentType =
                $"multipart/related; type=\"application/dicom\"; boundary={Boundary}";
            context.Request.Body = body;
            if (studyUid != null)
                context.Request.RouteValues["studyInstanceUID"] = studyUid;
            if (acceptHeader != null)
                context.Request.Headers["Accept"] = acceptHeader;
            return context;
        }

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

        private static byte[] BuildDicomBytes(
            string studyUid = "1.2.3",
            string sopClassUid = null,
            string sopInstanceUid = null)
        {
            var dataset = new DicomDataset().NotValidated();
            dataset.Add(DicomTag.StudyInstanceUID, studyUid);
            dataset.Add(DicomTag.SOPClassUID, sopClassUid ?? DicomUID.CTImageStorage.UID);
            dataset.Add(DicomTag.SOPInstanceUID, sopInstanceUid ?? DicomUID.Generate().UID);
            var file = new DicomFile(dataset);
            var ms = new MemoryStream();
            file.Save(ms);
            return ms.ToArray();
        }

        private static async Task<string> ReadBodyStringAsync(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        private static IDicomStowResponse MakeSuccess(string sopClassUid, string sopInstanceUid)
            => new DicomStowSuccessResponse(new List<DicomStowInstanceResult>
            {
                new DicomStowInstanceResult(sopClassUid, sopInstanceUid)
            });

        #endregion

        // ─── 501 Not Implemented ──────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleStow_NoProvider_Returns501()
        {
            var service = new NoStowProviderService();
            var context = BuildStowContext(new MemoryStream());

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        }

        // ─── 400 Bad Request (malformed body) ────────────────────────────────

        [FactForNetCore]
        public async Task HandleStow_MissingBoundary_Returns400()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(new List<DicomStowInstanceResult>())));

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/dicom"; // not multipart
            context.Request.Body = new MemoryStream();

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleStow_EmptyMultipartBody_Returns400()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(new List<DicomStowInstanceResult>())));

            // Build a multipart body with only the closing boundary (no parts).
            var ms = new MemoryStream();
            using (var w = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
            {
                w.Write($"--{Boundary}--\r\n");
                w.Flush();
            }
            ms.Position = 0;

            var context = BuildStowContext(ms);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleStow_CorruptDicomPart_Returns400()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(new List<DicomStowInstanceResult>())));

            var body = BuildMultipartBody(("application/dicom", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleStow_NonDicomContentTypePart_Returns400()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(new List<DicomStowInstanceResult>())));

            var body = BuildMultipartBody(("text/plain", new byte[] { 0x41 }));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        // ─── 503 Service Unavailable (provider throws) ───────────────────────

        [FactForNetCore]
        public async Task HandleStow_ProviderThrows_Returns503()
        {
            var service = new TestStowService((req, ct) =>
                throw new InvalidOperationException("storage unavailable"));

            var dicomBytes = BuildDicomBytes();
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        }

        // ─── 200 OK (full success) ────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleStow_SingleInstance_Returns200()
        {
            string sopClassUid = DicomUID.CTImageStorage.UID;
            string sopInstanceUid = DicomUID.Generate().UID;

            var service = new TestStowService((req, ct) =>
                Task.FromResult(MakeSuccess(sopClassUid, sopInstanceUid)));

            var dicomBytes = BuildDicomBytes(sopInstanceUid: sopInstanceUid);
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleStow_MultipleInstances_Returns200()
        {
            var service = new TestStowService((req, ct) =>
            {
                var stored = new List<DicomStowInstanceResult>();
                foreach (var file in req.Instances)
                {
                    stored.Add(new DicomStowInstanceResult(
                        file.Dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, string.Empty),
                        file.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty)));
                }
                return Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(stored));
            });

            var body = BuildMultipartBody(
                ("application/dicom", BuildDicomBytes(sopInstanceUid: "1.2.3.1")),
                ("application/dicom", BuildDicomBytes(sopInstanceUid: "1.2.3.2")));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        // ─── 409 Conflict (all instances fail UID validation) ────────────────

        [FactForNetCore]
        public async Task HandleStow_AllInstancesMismatchStudyUid_Returns409()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult(MakeSuccess(DicomUID.CTImageStorage.UID, DicomUID.Generate().UID)));

            // Instance has StudyUID "9.9.9" but route scopes to "1.2.3"
            var dicomBytes = BuildDicomBytes(studyUid: "9.9.9");
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body, studyUid: "1.2.3");

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        }

        // ─── 202 Accepted (partial success: some UID mismatches) ──────────────

        [FactForNetCore]
        public async Task HandleStow_PartialStudyUidMismatch_Returns202()
        {
            // Instance 1 matches, instance 2 does not.
            var service = new TestStowService((req, ct) =>
            {
                Assert.Single(req.Instances); // only 1 passes validation
                var stored = new List<DicomStowInstanceResult>
                {
                    new DicomStowInstanceResult(
                        req.Instances[0].Dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, string.Empty),
                        req.Instances[0].Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty))
                };
                return Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(stored));
            });

            var body = BuildMultipartBody(
                ("application/dicom", BuildDicomBytes(studyUid: "1.2.3", sopInstanceUid: "1.2.3.1")),
                ("application/dicom", BuildDicomBytes(studyUid: "9.9.9", sopInstanceUid: "1.2.3.2")));
            var context = BuildStowContext(body, studyUid: "1.2.3");

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        }

        [FactForNetCore]
        public async Task HandleStow_ProviderReturnsPartialSuccess_Returns202()
        {
            var service = new TestStowService((req, ct) =>
            {
                var stored = new List<DicomStowInstanceResult>
                {
                    new DicomStowInstanceResult(DicomUID.CTImageStorage.UID, "1.2.3.1")
                };
                var failed = new List<DicomStowInstanceResult>
                {
                    new DicomStowInstanceResult(DicomUID.CTImageStorage.UID, "1.2.3.2", 0x0110)
                };
                return Task.FromResult<IDicomStowResponse>(
                    new DicomStowPartialSuccessResponse(stored, failed));
            });

            var body = BuildMultipartBody(
                ("application/dicom", BuildDicomBytes(sopInstanceUid: "1.2.3.1")),
                ("application/dicom", BuildDicomBytes(sopInstanceUid: "1.2.3.2")));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        }

        // ─── Response body (XML) ─────────────────────────────────────────────

        [FactForNetCore]
        public async Task HandleStow_Success_ResponseBodyContainsReferencedSOPSequenceXml()
        {
            string sopInstanceUid = "1.2.840.99.1";
            var service = new TestStowService((req, ct) =>
                Task.FromResult(MakeSuccess(DicomUID.CTImageStorage.UID, sopInstanceUid)));

            var dicomBytes = BuildDicomBytes(sopInstanceUid: sopInstanceUid);
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            var responseXml = await ReadBodyStringAsync(context);
            Assert.Contains("00081199", responseXml);  // ReferencedSOPSequence tag
            Assert.Contains(sopInstanceUid, responseXml);
        }

        [FactForNetCore]
        public async Task HandleStow_Conflict_ResponseBodyContainsFailedSOPSequenceXml()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult(MakeSuccess(DicomUID.CTImageStorage.UID, DicomUID.Generate().UID)));

            var dicomBytes = BuildDicomBytes(studyUid: "9.9.9", sopInstanceUid: "1.9.9.1");
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body, studyUid: "1.2.3");

            await service.HandleStowRequestAsync(context);

            var responseXml = await ReadBodyStringAsync(context);
            Assert.Contains("00081198", responseXml);  // FailedSOPSequence tag
            Assert.Contains("1.9.9.1", responseXml);   // failed SOP Instance UID
        }

        // ─── Response body (JSON via Accept header) ──────────────────────────

        [FactForNetCore]
        public async Task HandleStow_AcceptDicomJson_ResponseBodyIsJson()
        {
            string sopInstanceUid = "1.2.840.99.2";
            var service = new TestStowService((req, ct) =>
                Task.FromResult(MakeSuccess(DicomUID.CTImageStorage.UID, sopInstanceUid)));

            var dicomBytes = BuildDicomBytes(sopInstanceUid: sopInstanceUid);
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body, acceptHeader: "application/dicom+json");

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Contains("application/dicom+json", context.Response.ContentType);
            var responseBody = await ReadBodyStringAsync(context);
            Assert.StartsWith("{", responseBody.TrimStart());
            Assert.Contains("00081199", responseBody);
            Assert.Contains(sopInstanceUid, responseBody);
        }

        // ─── Provider failure responses pass through ──────────────────────────

        [FactForNetCore]
        public async Task HandleStow_ProviderReturnsForbidden_Returns403()
        {
            var service = new TestStowService((req, ct) =>
                Task.FromResult<IDicomStowResponse>(new DicomWebForbiddenResponse()));

            var dicomBytes = BuildDicomBytes();
            var body = BuildMultipartBody(("application/dicom", dicomBytes));
            var context = BuildStowContext(body);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        // ─── Unscoped request passes all instances to provider ────────────────

        [FactForNetCore]
        public async Task HandleStow_UnscopedRequest_AllInstancesPassedToProvider()
        {
            int instanceCount = 0;
            var service = new TestStowService((req, ct) =>
            {
                instanceCount = req.Instances.Count;
                var stored = new List<DicomStowInstanceResult>();
                foreach (var f in req.Instances)
                    stored.Add(new DicomStowInstanceResult(
                        f.Dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, string.Empty),
                        f.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty)));
                return Task.FromResult<IDicomStowResponse>(new DicomStowSuccessResponse(stored));
            });

            // Two instances with different study UIDs — both should pass when unscoped.
            var body = BuildMultipartBody(
                ("application/dicom", BuildDicomBytes(studyUid: "1.1.1", sopInstanceUid: "1.1.1.1")),
                ("application/dicom", BuildDicomBytes(studyUid: "2.2.2", sopInstanceUid: "2.2.2.1")));
            // No studyUid in route → unscoped
            var context = BuildStowContext(body, studyUid: null);

            await service.HandleStowRequestAsync(context);

            Assert.Equal(2, instanceCount);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }
    }
}

#endif
