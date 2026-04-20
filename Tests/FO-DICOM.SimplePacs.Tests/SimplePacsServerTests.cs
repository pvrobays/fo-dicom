// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.AspNetCore;
using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.Serialization;
using FellowOakDicom.SimplePacs.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.SimplePacs.Tests
{
    /// <summary>
    /// End-to-end tests for <see cref="SimplePacsServer"/> using a real ASP.NET Core
    /// <see cref="TestServer"/>, an in-memory SQLite database, and a temporary file store.
    ///
    /// Each test class instance gets its own isolated host, database, and storage root so
    /// tests can run fully in parallel without interference.
    /// </summary>
    public sealed class SimplePacsServerTests : IAsyncLifetime
    {
        // ── Test infrastructure ───────────────────────────────────────────────

        private static readonly string TestDataDir =
            Path.Combine(AppContext.BaseDirectory, "Test Data");

        private IHost _host = null!;
        private HttpClient _client = null!;
        private SqliteConnection _keepAlive = null!;
        private string _storageRoot = null!;

        public async Task InitializeAsync()
        {
            // Each test instance gets a unique in-memory SQLite database.
            // The keep-alive connection prevents the database from being destroyed
            // between factory-created DbContext instances.
            _keepAlive = new SqliteConnection("Data Source=:memory:");
            _keepAlive.Open();

            _storageRoot = Path.Combine(
                Path.GetTempPath(), $"simplepacs-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_storageRoot);

            _host = new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();

                        services.AddDbContextFactory<SimplePacsDbContext>(opts =>
                            opts.UseSqlite(_keepAlive));

                        var config = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["SimplePacs:StorageRoot"] = _storageRoot
                            })
                            .Build();
                        services.AddSingleton<IConfiguration>(config);

                        services.AddSingleton<IDicomWebService>(provider =>
                            Microsoft.Extensions.DependencyInjection
                                .ActivatorUtilities.CreateInstance<SimplePacsServer>(provider));
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(ep => ep.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();

            // Create the schema before starting the host.
            using (var scope = _host.Services.CreateScope())
            {
                var factory = scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<SimplePacsDbContext>>();
                using var db = await factory.CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }

            await _host.StartAsync();
            _client = _host.GetTestServer().CreateClient();
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _keepAlive.Close();
            _keepAlive.Dispose();
            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }

        // ── DICOM / HTTP helpers ──────────────────────────────────────────────

        private const string StowBoundary = "e2e-simplepacs-boundary";

        /// <summary>
        /// Builds a minimal DICOM Part 10 byte array with the supplied metadata.
        /// No pixel data is included — just the tags SimplePacsServer indexes.
        /// </summary>
        private static byte[] MakeDicomBytes(
            string studyUid,
            string seriesUid,
            string sopUid,
            string modality = "CT",
            string patientName = "DOE^JOHN",
            string patientId = "PAT001",
            string patientBirthDate = "19800101",
            string patientSex = "M",
            string studyDate = "20250115",
            string studyTime = "120000",
            string accessionNumber = "ACC001",
            string seriesDescription = "Test Series",
            string seriesNumber = "1",
            string instanceNumber = "1",
            string studyId = "STUDY01",
            string referringPhysician = "SMITH^DR",
            string sopClassUid = "1.2.840.10008.5.1.4.1.1.2" /* CT Image Storage */)
        {
            var ds = new DicomDataset().NotValidated();
            ds.Add(DicomTag.StudyInstanceUID,        studyUid);
            ds.Add(DicomTag.SeriesInstanceUID,       seriesUid);
            ds.Add(DicomTag.SOPInstanceUID,          sopUid);
            ds.Add(DicomTag.SOPClassUID,             sopClassUid);
            ds.Add(DicomTag.Modality,                modality);
            ds.Add(DicomTag.PatientName,             patientName);
            ds.Add(DicomTag.PatientID,               patientId);
            ds.Add(DicomTag.PatientBirthDate,        patientBirthDate);
            ds.Add(DicomTag.PatientSex,              patientSex);
            ds.Add(DicomTag.StudyDate,               studyDate);
            ds.Add(DicomTag.StudyTime,               studyTime);
            ds.Add(DicomTag.AccessionNumber,         accessionNumber);
            ds.Add(DicomTag.SeriesDescription,       seriesDescription);
            ds.Add(DicomTag.SeriesNumber,            seriesNumber);
            ds.Add(DicomTag.InstanceNumber,          instanceNumber);
            ds.Add(DicomTag.StudyID,                 studyId);
            ds.Add(DicomTag.ReferringPhysicianName,  referringPhysician);
            var file = new DicomFile(ds);
            using var ms = new MemoryStream();
            file.Save(ms);
            return ms.ToArray();
        }

        private static MultipartContent MakeStowContent(params byte[][] parts)
        {
            var content = new MultipartContent("related", StowBoundary);
            foreach (var part in parts)
            {
                var pc = new ByteArrayContent(part);
                pc.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/dicom");
                content.Add(pc);
            }
            return content;
        }

        private async Task<HttpResponseMessage> StowAsync(
            MultipartContent content, string? studyUid = null)
        {
            var url = studyUid != null
                ? $"/dicomweb/studies/{studyUid}"
                : "/dicomweb/studies";
            return await _client.PostAsync(url, content);
        }

        private async Task<DicomDataset[]> QidoAsync(string relativeUrl)
        {
            var resp = await _client.GetAsync(relativeUrl);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return DicomJson.ConvertJsonToDicomArray(json) ?? Array.Empty<DicomDataset>();
        }

        /// <summary>
        /// Retrieves WADO-RS instance parts as raw <see cref="DicomFile"/> objects.
        /// Requests transfer-syntax=* so the server returns files in their native
        /// transfer syntax without transcoding.
        /// </summary>
        private async Task<DicomFile[]> WadoInstancesAsync(string relativeUrl)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            var resp = await _client.SendAsync(request);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return Array.Empty<DicomFile>();
            resp.EnsureSuccessStatusCode();
            return await ParseMultipartDicomAsync(resp);
        }

        private async Task<DicomDataset[]> WadoMetadataAsync(string relativeUrl)
        {
            var resp = await _client.GetAsync(relativeUrl);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return Array.Empty<DicomDataset>();
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return DicomJson.ConvertJsonToDicomArray(json) ?? Array.Empty<DicomDataset>();
        }

        private static async Task<DicomFile[]> ParseMultipartDicomAsync(
            HttpResponseMessage resp)
        {
            // Extract boundary from Content-Type header.
            var ct = resp.Content.Headers.ContentType?.ToString() ?? string.Empty;
            var boundary = ExtractBoundary(ct);

            // Buffer the full response so the TestServer can finish writing
            // before we start reading multipart sections. Without buffering,
            // MultipartReaderStream sees an "unexpected end of stream" because
            // the test host flushes asynchronously.
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var stream = new MemoryStream(bytes);
            var reader = new Microsoft.AspNetCore.WebUtilities.MultipartReader(boundary, stream);

            var files = new List<DicomFile>();
            Microsoft.AspNetCore.WebUtilities.MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms);
                ms.Position = 0;
                if (ms.Length > 0)
                    files.Add(await DicomFile.OpenAsync(ms, FileReadOption.ReadAll));
            }
            return files.ToArray();
        }

        private static string ExtractBoundary(string contentType)
        {
            // Content-Type: multipart/related; type="application/dicom"; boundary="xxxx"
            // The boundary value in the header is what MultipartReader expects (no "--" prefix).
            foreach (var part in contentType.Split(';'))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring("boundary=".Length).Trim('"');
                }
            }
            throw new InvalidOperationException($"No boundary in Content-Type: {contentType}");
        }

        private static byte[] LoadTestFile(string fileName) =>
            File.ReadAllBytes(Path.Combine(TestDataDir, fileName));

        // Deterministic UID generation for tests (no collisions between instances).
        private static string Uid(string suffix) => $"1.2.840.99999.{suffix}";

        private static string GetTag(DicomDataset ds, DicomTag tag) =>
            ds.GetSingleValueOrDefault(tag, string.Empty);

        /// <summary>
        /// Returns all values of a multi-value DICOM tag joined with backslash,
        /// e.g. "CT\MR" for a ModalitiesInStudy element with two values.
        /// Falls back to GetTag for single-value elements.
        /// </summary>
        private static string GetMultiTag(DicomDataset ds, DicomTag tag)
        {
            if (!ds.Contains(tag)) return string.Empty;
            var values = ds.GetValues<string>(tag);
            return values == null || values.Length == 0 ? string.Empty : string.Join("\\", values);
        }

        // ── STOW-RS tests ─────────────────────────────────────────────────────

        [Fact]
        public async Task Stow_SingleInstance_Returns200()
        {
            var bytes = MakeDicomBytes(Uid("1.1"), Uid("1.1.1"), Uid("1.1.1.1"));
            var resp = await StowAsync(MakeStowContent(bytes));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Stow_MultipleInstancesSameStudy_Returns200()
        {
            var studyUid = Uid("2.1");
            var seriesUid = Uid("2.1.1");
            var bytes1 = MakeDicomBytes(studyUid, seriesUid, Uid("2.1.1.1"), instanceNumber: "1");
            var bytes2 = MakeDicomBytes(studyUid, seriesUid, Uid("2.1.1.2"), instanceNumber: "2");
            var bytes3 = MakeDicomBytes(studyUid, seriesUid, Uid("2.1.1.3"), instanceNumber: "3");
            var resp = await StowAsync(MakeStowContent(bytes1, bytes2, bytes3));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Stow_MultipleStudies_Returns200()
        {
            var bytes1 = MakeDicomBytes(Uid("3.1"), Uid("3.1.1"), Uid("3.1.1.1"));
            var bytes2 = MakeDicomBytes(Uid("3.2"), Uid("3.2.1"), Uid("3.2.1.1"));
            var resp = await StowAsync(MakeStowContent(bytes1, bytes2));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Stow_MissingRequiredUid_ReturnsPartialSuccess()
        {
            var good = MakeDicomBytes(Uid("4.1"), Uid("4.1.1"), Uid("4.1.1.1"));

            // Craft a file whose StudyInstanceUID is empty (stripped by fo-dicom UI VR
            // handling) so that GetSingleValueOrDefault returns null — the server must
            // reject it with 0x0110 and report a partial-success 202 response.
            // We put a placeholder in the non-validated dataset to allow DicomFile creation,
            // then overwrite the stored bytes with a dataset that GetSingleValueOrDefault
            // returns null for StudyInstanceUID.
            //
            // Simplest approach: use a valid file whose StudyInstanceUID is a well-known
            // "empty UID" marker that the server treats as missing.
            // fo-dicom strips empty-string values on UI VRs, so we use AddOrUpdate with
            // the dataset in NotValidated mode and provide a placeholder UID, then use a
            // second dataset where StudyInstanceUID is genuinely absent.
            var badDs = new DicomDataset().NotValidated();
            // Add a placeholder SOP UID so DicomFileMetaInformation can be constructed.
            badDs.Add(DicomTag.SOPClassUID,       "1.2.840.10008.5.1.4.1.1.2");
            badDs.Add(DicomTag.SOPInstanceUID,    Uid("4.2.1.1"));
            badDs.Add(DicomTag.SeriesInstanceUID, Uid("4.2.1"));
            // Deliberately omit StudyInstanceUID → IsNullOrEmpty check rejects it
            var badFile = new DicomFile(badDs);
            using var ms = new MemoryStream();
            badFile.Save(ms);
            var bad = ms.ToArray();

            var resp = await StowAsync(MakeStowContent(good, bad));
            Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode); // 202 Partial
        }

        [Fact]
        public async Task Stow_DuplicateSop_OverwritesAndReturns200()
        {
            var studyUid  = Uid("5.1");
            var seriesUid = Uid("5.1.1");
            var sopUid    = Uid("5.1.1.1");

            // Store first version
            var v1 = MakeDicomBytes(studyUid, seriesUid, sopUid,
                patientName: "ORIGINAL^PATIENT", accessionNumber: "ACC-V1");
            var r1 = await StowAsync(MakeStowContent(v1));
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

            // Store updated version (same SOP UID, different patient name)
            var v2 = MakeDicomBytes(studyUid, seriesUid, sopUid,
                patientName: "UPDATED^PATIENT", accessionNumber: "ACC-V2");
            var r2 = await StowAsync(MakeStowContent(v2));
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

            // QIDO should reflect the updated metadata
            var results = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(results);
            Assert.Equal("UPDATED^PATIENT",
                GetTag(results[0], DicomTag.PatientName));
            Assert.Equal("ACC-V2",
                GetTag(results[0], DicomTag.AccessionNumber));
        }

        [Fact]
        public async Task Stow_StudyScoped_AcceptsMatchingStudy()
        {
            var studyUid = Uid("6.1");
            var bytes = MakeDicomBytes(studyUid, Uid("6.1.1"), Uid("6.1.1.1"));
            var resp = await StowAsync(MakeStowContent(bytes), studyUid: studyUid);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Stow_StudyScoped_RejectsNonMatchingStudy()
        {
            // Route UID doesn't match the UID inside the file → framework rejects it
            var bytes = MakeDicomBytes(Uid("7.1"), Uid("7.1.1"), Uid("7.1.1.1"));
            var resp = await StowAsync(MakeStowContent(bytes), studyUid: Uid("7.DIFFERENT"));
            // All instances failed UID scope validation → 409 Conflict (nothing stored)
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }

        [Fact]
        public async Task Stow_EmptyBody_Returns400()
        {
            // Send a multipart body with no parts (no valid DICOM content)
            var content = new StringContent(string.Empty);
            content.Headers.ContentType =
                System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                    "multipart/related; boundary=empty");
            var resp = await _client.PostAsync("/dicomweb/studies", content);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // ── QIDO-RS study-level tests ─────────────────────────────────────────

        [Fact]
        public async Task QidoStudy_NoFilter_ReturnsAllStudies()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("10.1"), Uid("10.1.1"), Uid("10.1.1.1")),
                MakeDicomBytes(Uid("10.2"), Uid("10.2.1"), Uid("10.2.1.1")),
                MakeDicomBytes(Uid("10.3"), Uid("10.3.1"), Uid("10.3.1.1"))));

            var results = await QidoAsync("/dicomweb/studies");
            // At least 3 studies — could be more if other tests added data, but since
            // each test gets its own isolated DB this will be exactly 3.
            Assert.Equal(3, results.Length);
        }

        [Fact]
        public async Task QidoStudy_ByStudyInstanceUid_ReturnsSingleStudy()
        {
            var targetUid = Uid("11.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("11.1"), Uid("11.1.1"), Uid("11.1.1.1")),
                MakeDicomBytes(targetUid,  Uid("11.2.1"), Uid("11.2.1.1")),
                MakeDicomBytes(Uid("11.3"), Uid("11.3.1"), Uid("11.3.1.1"))));

            var results = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(targetUid)}");
            Assert.Single(results);
            Assert.Equal(targetUid, GetTag(results[0], DicomTag.StudyInstanceUID));
        }

        [Fact]
        public async Task QidoStudy_ByPatientId_ReturnsMatching()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("12.1"), Uid("12.1.1"), Uid("12.1.1.1"), patientId: "PID-ALPHA"),
                MakeDicomBytes(Uid("12.2"), Uid("12.2.1"), Uid("12.2.1.1"), patientId: "PID-BETA"),
                MakeDicomBytes(Uid("12.3"), Uid("12.3.1"), Uid("12.3.1.1"), patientId: "PID-ALPHA")));

            var results = await QidoAsync("/dicomweb/studies?00100020=PID-ALPHA");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.Equal("PID-ALPHA", GetTag(r, DicomTag.PatientID)));
        }

        [Fact]
        public async Task QidoStudy_ByPatientNameWildcard_ReturnsMatching()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("13.1"), Uid("13.1.1"), Uid("13.1.1.1"), patientName: "DOE^JOHN"),
                MakeDicomBytes(Uid("13.2"), Uid("13.2.1"), Uid("13.2.1.1"), patientName: "DOE^JANE"),
                MakeDicomBytes(Uid("13.3"), Uid("13.3.1"), Uid("13.3.1.1"), patientName: "SMITH^BOB")));

            // Wildcard: DOE* matches both DOE^JOHN and DOE^JANE
            var results = await QidoAsync("/dicomweb/studies?00100010=DOE*");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.StartsWith("DOE", GetTag(r, DicomTag.PatientName)));
        }

        [Fact]
        public async Task QidoStudy_ByStudyDateExact_ReturnsMatching()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("14.1"), Uid("14.1.1"), Uid("14.1.1.1"), studyDate: "20250101"),
                MakeDicomBytes(Uid("14.2"), Uid("14.2.1"), Uid("14.2.1.1"), studyDate: "20250201"),
                MakeDicomBytes(Uid("14.3"), Uid("14.3.1"), Uid("14.3.1.1"), studyDate: "20250301")));

            var results = await QidoAsync("/dicomweb/studies?00080020=20250201");
            Assert.Single(results);
            Assert.Equal("20250201", GetTag(results[0], DicomTag.StudyDate));
        }

        [Fact]
        public async Task QidoStudy_ByStudyDateRange_ReturnsMatching()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("15.1"), Uid("15.1.1"), Uid("15.1.1.1"), studyDate: "20241231"),
                MakeDicomBytes(Uid("15.2"), Uid("15.2.1"), Uid("15.2.1.1"), studyDate: "20250115"),
                MakeDicomBytes(Uid("15.3"), Uid("15.3.1"), Uid("15.3.1.1"), studyDate: "20250215"),
                MakeDicomBytes(Uid("15.4"), Uid("15.4.1"), Uid("15.4.1.1"), studyDate: "20250301")));

            // Range: 20250101 - 20250228 → should return only the two January/February studies
            var results = await QidoAsync("/dicomweb/studies?00080020=20250101-20250228");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
            {
                var d = GetTag(r, DicomTag.StudyDate);
                Assert.True(string.Compare(d, "20250101", StringComparison.Ordinal) >= 0);
                Assert.True(string.Compare(d, "20250228", StringComparison.Ordinal) <= 0);
            });
        }

        [Fact]
        public async Task QidoStudy_ByModalitiesInStudy_ReturnsMatching()
        {
            // Two CT studies and one MR study.
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("16.1"), Uid("16.1.1"), Uid("16.1.1.1"), modality: "CT"),
                MakeDicomBytes(Uid("16.2"), Uid("16.2.1"), Uid("16.2.1.1"), modality: "MR"),
                MakeDicomBytes(Uid("16.3"), Uid("16.3.1"), Uid("16.3.1.1"), modality: "CT")));

            var results = await QidoAsync("/dicomweb/studies?00080061=CT");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.Contains("CT", GetTag(r, DicomTag.ModalitiesInStudy)));
        }

        [Fact]
        public async Task QidoStudy_ModalitiesInStudyDelimiterSafe_NoFalseMatch()
        {
            // Store a study with modality "MRI" — must NOT match a filter for "MR"
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("17.1"), Uid("17.1.1"), Uid("17.1.1.1"), modality: "MRI"),
                MakeDicomBytes(Uid("17.2"), Uid("17.2.1"), Uid("17.2.1.1"), modality: "MR")));

            var results = await QidoAsync("/dicomweb/studies?00080061=MR");
            // Only the study with exactly "MR" modality should match, not the "MRI" one.
            Assert.Single(results);
            Assert.Equal("MR", GetTag(results[0], DicomTag.ModalitiesInStudy));
        }

        [Fact]
        public async Task QidoStudy_Pagination_OffsetAndLimit()
        {
            // Store 5 studies with deterministic, sortable UIDs
            for (int i = 1; i <= 5; i++)
                await StowAsync(MakeStowContent(
                    MakeDicomBytes(Uid($"18.{i}"), Uid($"18.{i}.1"), Uid($"18.{i}.1.1"))));

            // Limit=2: first 2 results
            var page1 = await QidoAsync("/dicomweb/studies?limit=2");
            Assert.Equal(2, page1.Length);

            // Offset=2 Limit=2: next 2
            var page2 = await QidoAsync("/dicomweb/studies?limit=2&offset=2");
            Assert.Equal(2, page2.Length);

            // The two pages must not overlap
            var uids1 = page1.Select(r => GetTag(r, DicomTag.StudyInstanceUID)).ToHashSet();
            var uids2 = page2.Select(r => GetTag(r, DicomTag.StudyInstanceUID)).ToHashSet();
            Assert.Empty(uids1.Intersect(uids2));
        }

        [Fact]
        public async Task QidoStudy_ResponseContainsAllRequiredFields()
        {
            await StowAsync(MakeStowContent(
                MakeDicomBytes(Uid("19.1"), Uid("19.1.1"), Uid("19.1.1.1"),
                    patientName: "FIELDS^TEST", patientId: "FT001",
                    studyDate: "20250115", accessionNumber: "ACC-FIELDS")));

            var results = await QidoAsync("/dicomweb/studies");
            Assert.Single(results);
            var ds = results[0];

            // All 14 PS3.18 required study-level return keys must be present.
            Assert.True(ds.Contains(DicomTag.StudyInstanceUID));
            Assert.True(ds.Contains(DicomTag.StudyDate));
            Assert.True(ds.Contains(DicomTag.StudyTime));
            Assert.True(ds.Contains(DicomTag.AccessionNumber));
            Assert.True(ds.Contains(DicomTag.ReferringPhysicianName));
            Assert.True(ds.Contains(DicomTag.PatientName));
            Assert.True(ds.Contains(DicomTag.PatientID));
            Assert.True(ds.Contains(DicomTag.PatientBirthDate));
            Assert.True(ds.Contains(DicomTag.PatientSex));
            Assert.True(ds.Contains(DicomTag.StudyID));
            Assert.True(ds.Contains(DicomTag.ModalitiesInStudy));
            Assert.True(ds.Contains(DicomTag.InstanceAvailability));
            Assert.True(ds.Contains(DicomTag.NumberOfStudyRelatedSeries));
            Assert.True(ds.Contains(DicomTag.NumberOfStudyRelatedInstances));

            Assert.Equal("ONLINE", GetTag(ds, DicomTag.InstanceAvailability));
        }

        // ── QIDO-RS series-level tests ────────────────────────────────────────

        [Fact]
        public async Task QidoSeries_ByStudyUid_ReturnsSeriesInStudy()
        {
            var studyA = Uid("20.1");
            var studyB = Uid("20.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyA, Uid("20.1.1"), Uid("20.1.1.1")),
                MakeDicomBytes(studyA, Uid("20.1.2"), Uid("20.1.2.1")),
                MakeDicomBytes(studyB, Uid("20.2.1"), Uid("20.2.1.1"))));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyA)}/series");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.Equal(studyA, GetTag(r, DicomTag.StudyInstanceUID)));
        }

        [Fact]
        public async Task QidoSeries_ByModality_ReturnsMatching()
        {
            var studyUid = Uid("21.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, Uid("21.1.1"), Uid("21.1.1.1"), modality: "CT"),
                MakeDicomBytes(studyUid, Uid("21.1.2"), Uid("21.1.2.1"), modality: "MR"),
                MakeDicomBytes(studyUid, Uid("21.1.3"), Uid("21.1.3.1"), modality: "CT")));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}/series?00080060=CT");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.Equal("CT", GetTag(r, DicomTag.Modality)));
        }

        [Fact]
        public async Task QidoSeries_BySeriesUid_ReturnsSingle()
        {
            var studyUid  = Uid("22.1");
            var targetSer = Uid("22.1.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, Uid("22.1.1"),  Uid("22.1.1.1")),
                MakeDicomBytes(studyUid, targetSer,       Uid("22.1.2.1")),
                MakeDicomBytes(studyUid, Uid("22.1.3"),  Uid("22.1.3.1"))));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}/series" +
                $"?0020000E={Uri.EscapeDataString(targetSer)}");
            Assert.Single(results);
            Assert.Equal(targetSer, GetTag(results[0], DicomTag.SeriesInstanceUID));
        }

        [Fact]
        public async Task QidoSeries_ResponseContainsAllRequiredFields()
        {
            var studyUid = Uid("23.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, Uid("23.1.1"), Uid("23.1.1.1"),
                    modality: "CT", seriesDescription: "Chest", seriesNumber: "3")));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}/series");
            Assert.Single(results);
            var ds = results[0];

            Assert.True(ds.Contains(DicomTag.StudyInstanceUID));
            Assert.True(ds.Contains(DicomTag.SeriesInstanceUID));
            Assert.True(ds.Contains(DicomTag.Modality));
            Assert.True(ds.Contains(DicomTag.SeriesDescription));
            Assert.True(ds.Contains(DicomTag.SeriesNumber));
            Assert.True(ds.Contains(DicomTag.InstanceAvailability));
            Assert.True(ds.Contains(DicomTag.NumberOfSeriesRelatedInstances));
            Assert.Equal("CT",     GetTag(ds, DicomTag.Modality));
            Assert.Equal("Chest",  GetTag(ds, DicomTag.SeriesDescription));
            Assert.Equal("ONLINE", GetTag(ds, DicomTag.InstanceAvailability));
        }

        // ── QIDO-RS instance-level tests ──────────────────────────────────────

        [Fact]
        public async Task QidoInstance_ByStudyAndSeries_ReturnsInstancesInSeries()
        {
            var studyUid  = Uid("30.1");
            var seriesA   = Uid("30.1.1");
            var seriesB   = Uid("30.1.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesA, Uid("30.1.1.1")),
                MakeDicomBytes(studyUid, seriesA, Uid("30.1.1.2")),
                MakeDicomBytes(studyUid, seriesB, Uid("30.1.2.1"))));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesA)}/instances");
            Assert.Equal(2, results.Length);
            Assert.All(results, r =>
                Assert.Equal(seriesA, GetTag(r, DicomTag.SeriesInstanceUID)));
        }

        [Fact]
        public async Task QidoInstance_BySopInstanceUid_ReturnsSingle()
        {
            var studyUid  = Uid("31.1");
            var seriesUid = Uid("31.1.1");
            var targetSop = Uid("31.1.1.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesUid, Uid("31.1.1.1")),
                MakeDicomBytes(studyUid, seriesUid, targetSop),
                MakeDicomBytes(studyUid, seriesUid, Uid("31.1.1.3"))));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances?00080018={Uri.EscapeDataString(targetSop)}");
            Assert.Single(results);
            Assert.Equal(targetSop, GetTag(results[0], DicomTag.SOPInstanceUID));
        }

        [Fact]
        public async Task QidoInstance_ResponseContainsAllRequiredFields()
        {
            var studyUid  = Uid("32.1");
            var seriesUid = Uid("32.1.1");
            var sopUid    = Uid("32.1.1.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesUid, sopUid,
                    instanceNumber: "42",
                    sopClassUid: "1.2.840.10008.5.1.4.1.1.2")));

            var results = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}/instances");
            Assert.Single(results);
            var ds = results[0];

            Assert.True(ds.Contains(DicomTag.StudyInstanceUID));
            Assert.True(ds.Contains(DicomTag.SeriesInstanceUID));
            Assert.True(ds.Contains(DicomTag.SOPInstanceUID));
            Assert.True(ds.Contains(DicomTag.SOPClassUID));
            Assert.True(ds.Contains(DicomTag.InstanceNumber));
            Assert.True(ds.Contains(DicomTag.InstanceAvailability));
            Assert.Equal(sopUid,  GetTag(ds, DicomTag.SOPInstanceUID));
            Assert.Equal("42",    GetTag(ds, DicomTag.InstanceNumber));
            Assert.Equal("ONLINE", GetTag(ds, DicomTag.InstanceAvailability));
        }

        // ── WADO-RS instance tests ────────────────────────────────────────────

        [Fact]
        public async Task WadoInstance_StudyLevel_ReturnsAllInstancesInStudy()
        {
            var studyUid = Uid("40.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, Uid("40.1.1"), Uid("40.1.1.1")),
                MakeDicomBytes(studyUid, Uid("40.1.2"), Uid("40.1.2.1")),
                MakeDicomBytes(studyUid, Uid("40.1.2"), Uid("40.1.2.2"))));

            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}");
            Assert.Equal(3, files.Length);
            Assert.All(files, f =>
                Assert.Equal(studyUid,
                    f.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty)));
        }

        [Fact]
        public async Task WadoInstance_SeriesLevel_ReturnsInstancesInSeries()
        {
            var studyUid  = Uid("41.1");
            var seriesA   = Uid("41.1.1");
            var seriesB   = Uid("41.1.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesA, Uid("41.1.1.1")),
                MakeDicomBytes(studyUid, seriesA, Uid("41.1.1.2")),
                MakeDicomBytes(studyUid, seriesB, Uid("41.1.2.1"))));

            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesA)}");
            Assert.Equal(2, files.Length);
            Assert.All(files, f =>
                Assert.Equal(seriesA,
                    f.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty)));
        }

        [Fact]
        public async Task WadoInstance_InstanceLevel_ReturnsSingleInstance()
        {
            var studyUid  = Uid("42.1");
            var seriesUid = Uid("42.1.1");
            var sopUid    = Uid("42.1.1.2");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesUid, Uid("42.1.1.1")),
                MakeDicomBytes(studyUid, seriesUid, sopUid),
                MakeDicomBytes(studyUid, seriesUid, Uid("42.1.1.3"))));

            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}");
            Assert.Single(files);
            Assert.Equal(sopUid,
                files[0].Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty));
        }

        [Fact]
        public async Task WadoInstance_NonExistentStudy_Returns404()
        {
            var resp = await _client.GetAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(Uid("99.NONEXISTENT"))}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task WadoInstance_RetrievedFileMatchesStored()
        {
            var studyUid  = Uid("43.1");
            var seriesUid = Uid("43.1.1");
            var sopUid    = Uid("43.1.1.1");
            var original  = MakeDicomBytes(studyUid, seriesUid, sopUid,
                patientName: "WADO^ROUNDTRIP", accessionNumber: "WACC001");
            await StowAsync(MakeStowContent(original));

            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}");
            Assert.Single(files);
            var ds = files[0].Dataset;
            Assert.Equal("WADO^ROUNDTRIP", ds.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
            Assert.Equal("WACC001",         ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty));
        }

        // ── WADO-RS metadata tests ────────────────────────────────────────────

        [Fact]
        public async Task WadoMetadata_StudyLevel_ReturnsAllDatasetsInStudy()
        {
            var studyUid = Uid("50.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, Uid("50.1.1"), Uid("50.1.1.1")),
                MakeDicomBytes(studyUid, Uid("50.1.1"), Uid("50.1.1.2")),
                MakeDicomBytes(studyUid, Uid("50.1.2"), Uid("50.1.2.1"))));

            var datasets = await WadoMetadataAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}/metadata");
            Assert.Equal(3, datasets.Length);
            Assert.All(datasets, ds =>
                Assert.Equal(studyUid,
                    ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty)));
        }

        [Fact]
        public async Task WadoMetadata_InstanceLevel_ReturnsSingleDataset()
        {
            var studyUid  = Uid("51.1");
            var seriesUid = Uid("51.1.1");
            var sopUid    = Uid("51.1.1.1");
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesUid, sopUid, patientName: "META^TEST")));

            var datasets = await WadoMetadataAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}/metadata");
            Assert.Single(datasets);
            Assert.Equal("META^TEST",
                datasets[0].GetSingleValueOrDefault(DicomTag.PatientName, string.Empty));
        }

        [Fact]
        public async Task WadoMetadata_NonExistentStudy_Returns404()
        {
            var resp = await _client.GetAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(Uid("99.NOMETA"))}/metadata");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // ── Round-trip tests ──────────────────────────────────────────────────

        [Fact]
        public async Task RoundTrip_StowThenQidoThenWado_AllFieldsConsistent()
        {
            var studyUid   = Uid("60.1");
            var seriesCt   = Uid("60.1.1");
            var seriesMr   = Uid("60.1.2");
            var sopCt      = Uid("60.1.1.1");
            var sopMr      = Uid("60.1.2.1");

            // STOW two instances: one CT, one MR — same patient, same study
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, seriesCt, sopCt,
                    modality: "CT",
                    patientName: "ROUNDTRIP^PATIENT",
                    patientId: "RT001",
                    studyDate: "20250115",
                    accessionNumber: "RT-ACC"),
                MakeDicomBytes(studyUid, seriesMr, sopMr,
                    modality: "MR",
                    patientName: "ROUNDTRIP^PATIENT",
                    patientId: "RT001",
                    studyDate: "20250115",
                    accessionNumber: "RT-ACC")));

            // QIDO study → verify counts and ModalitiesInStudy
            var studyResults = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(studyResults);
            var studyDs = studyResults[0];
            Assert.Equal("ROUNDTRIP^PATIENT", GetTag(studyDs, DicomTag.PatientName));
            Assert.Equal("RT001",             GetTag(studyDs, DicomTag.PatientID));
            Assert.Equal("20250115",          GetTag(studyDs, DicomTag.StudyDate));
            Assert.Equal("RT-ACC",            GetTag(studyDs, DicomTag.AccessionNumber));
            // Two series, two instances
            Assert.Equal("2", GetTag(studyDs, DicomTag.NumberOfStudyRelatedSeries));
            Assert.Equal("2", GetTag(studyDs, DicomTag.NumberOfStudyRelatedInstances));
            // ModalitiesInStudy should be "CT\MR" (sorted alphabetically, backslash-separated)
            Assert.Equal(@"CT\MR", GetMultiTag(studyDs, DicomTag.ModalitiesInStudy));

            // QIDO series → verify both series found
            var seriesResults = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}/series");
            Assert.Equal(2, seriesResults.Length);

            // WADO study-level → verify both files returned and parseable
            var wadoFiles = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}");
            Assert.Equal(2, wadoFiles.Length);
            var sopUids = wadoFiles
                .Select(f => f.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty))
                .OrderBy(u => u)
                .ToArray();
            Assert.Contains(sopCt, sopUids);
            Assert.Contains(sopMr, sopUids);
        }

        [Fact]
        public async Task RoundTrip_StudyCounts_CorrectAfterMultipleStows()
        {
            var studyUid  = Uid("61.1");
            var series1   = Uid("61.1.1");
            var series2   = Uid("61.1.2");

            // STOW 1 instance in 1 series
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, series1, Uid("61.1.1.1"), modality: "CT")));

            var r1 = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(r1);
            Assert.Equal("1", GetTag(r1[0], DicomTag.NumberOfStudyRelatedSeries));
            Assert.Equal("1", GetTag(r1[0], DicomTag.NumberOfStudyRelatedInstances));
            Assert.Equal("CT", GetTag(r1[0], DicomTag.ModalitiesInStudy));

            // STOW 2 more instances in a second series (different modality)
            await StowAsync(MakeStowContent(
                MakeDicomBytes(studyUid, series2, Uid("61.1.2.1"), modality: "MR"),
                MakeDicomBytes(studyUid, series2, Uid("61.1.2.2"), modality: "MR")));

            var r2 = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(r2);
            Assert.Equal("2", GetTag(r2[0], DicomTag.NumberOfStudyRelatedSeries));
            Assert.Equal("3", GetTag(r2[0], DicomTag.NumberOfStudyRelatedInstances));
            // Both modalities now present, sorted
            Assert.Equal(@"CT\MR", GetMultiTag(r2[0], DicomTag.ModalitiesInStudy));
        }

        // ── Real-file tests ───────────────────────────────────────────────────

        [Fact]
        public async Task RealFile_RichMetadataRoundTrip()
        {
            // GH064.dcm — MR file with all standard metadata tags populated.
            var bytes = LoadTestFile("GH064.dcm");

            // Parse the original file so we know the expected tag values.
            using var ms = new MemoryStream(bytes);
            var original = await DicomFile.OpenAsync(ms);
            var origDs = original.Dataset;
            var studyUid  = origDs.GetSingleValueOrDefault(DicomTag.StudyInstanceUID,  string.Empty);
            var seriesUid = origDs.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
            var sopUid    = origDs.GetSingleValueOrDefault(DicomTag.SOPInstanceUID,    string.Empty);

            // STOW
            var resp = await StowAsync(MakeStowContent(bytes));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // QIDO study — verify key indexed fields match original dataset
            var studyResults = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(studyResults);
            var studyDs = studyResults[0];

            AssertTagMatches(origDs, studyDs, DicomTag.PatientName);
            AssertTagMatches(origDs, studyDs, DicomTag.PatientID);
            AssertTagMatches(origDs, studyDs, DicomTag.StudyDate);
            AssertTagMatches(origDs, studyDs, DicomTag.AccessionNumber);
            AssertTagMatches(origDs, studyDs, DicomTag.StudyInstanceUID);

            // WADO instance-level — verify retrieved file is parseable and UIDs match
            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}");
            Assert.Single(files);
            Assert.Equal(studyUid,
                files[0].Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal(sopUid,
                files[0].Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty));
        }

        [Fact]
        public async Task RealFile_MultiInstanceStudy()
        {
            // GH178.dcm, GH179A.dcm, GH179B.dcm all share the same SopInstanceUID so they
            // cannot be stored as 3 distinct instances. Instead, build 3 synthetic CT instances
            // that share the same StudyInstanceUID and SeriesInstanceUID but have distinct
            // SopInstanceUIDs — this is the correct way to represent 3 instances in one study.
            var studyUid  = Uid("99.178.1");
            var seriesUid = Uid("99.178.2");
            var sop1 = Uid("99.178.3.1");
            var sop2 = Uid("99.178.3.2");
            var sop3 = Uid("99.178.3.3");

            var bytes1 = MakeDicomBytes(studyUid, seriesUid, sop1, modality: "CT", instanceNumber: "1");
            var bytes2 = MakeDicomBytes(studyUid, seriesUid, sop2, modality: "CT", instanceNumber: "2");
            var bytes3 = MakeDicomBytes(studyUid, seriesUid, sop3, modality: "CT", instanceNumber: "3");

            // STOW all three in one request
            var resp = await StowAsync(MakeStowContent(bytes1, bytes2, bytes3));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // WADO study-level → must return all 3 instances
            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}");
            Assert.Equal(3, files.Length);
            Assert.All(files, f =>
                Assert.Equal(studyUid,
                    f.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty)));

            // QIDO study → NumberOfStudyRelatedInstances = 3
            var studyResults = await QidoAsync(
                $"/dicomweb/studies?0020000D={Uri.EscapeDataString(studyUid)}");
            Assert.Single(studyResults);
            Assert.Equal("3",
                GetTag(studyResults[0], DicomTag.NumberOfStudyRelatedInstances));
        }

        [Fact]
        public async Task RealFile_EncapsulatedTransferSyntax()
        {
            // GH538-jpeg1.dcm — JPEG Process 1 (encapsulated transfer syntax).
            // SimplePacsServer must store and retrieve it without any transcoding.
            var bytes = LoadTestFile("GH538-jpeg1.dcm");

            using var msOrig = new MemoryStream(bytes);
            var origFile = await DicomFile.OpenAsync(msOrig, FileReadOption.ReadAll);
            var studyUid  = origFile.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID,  string.Empty);
            var seriesUid = origFile.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
            var sopUid    = origFile.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID,    string.Empty);

            // STOW
            var resp = await StowAsync(MakeStowContent(bytes));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // WADO instance → retrieve and verify transfer syntax preserved
            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(studyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}");
            Assert.Single(files);

            // The retrieved file should still be in the original transfer syntax (no transcoding).
            var retrievedTs = files[0].FileMetaInfo?.TransferSyntax;
            var originalTs  = origFile.FileMetaInfo?.TransferSyntax;
            Assert.NotNull(retrievedTs);
            Assert.Equal(originalTs?.UID?.UID, retrievedTs?.UID?.UID);
        }

        [Fact]
        public async Task RealFile_MultiframeFile()
        {
            // multiframe.dcm — SC modality with multiple frames.
            // The file has no StudyInstanceUID, so we inject one before storing.
            var rawBytes = LoadTestFile("multiframe.dcm");

            using var msOrig = new MemoryStream(rawBytes);
            var origFile = await DicomFile.OpenAsync(msOrig, FileReadOption.ReadAll);

            // Inject a StudyInstanceUID so the server can index the file.
            // Must be digits-and-dots only (DICOM UI VR).
            var injectedStudyUid = "1.2.840.99999.70.1.1";
            origFile.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, injectedStudyUid);

            var seriesUid = origFile.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
            var sopUid    = origFile.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID,    string.Empty);
            var origFrames = origFile.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);

            // Re-serialise with the injected UID
            byte[] bytes;
            using (var msOut = new MemoryStream())
            {
                origFile.Save(msOut);
                bytes = msOut.ToArray();
            }

            // STOW
            var resp = await StowAsync(MakeStowContent(bytes));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // WADO instance → retrieve and verify it parses as a multi-frame file
            var files = await WadoInstancesAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(injectedStudyUid)}" +
                $"/series/{Uri.EscapeDataString(seriesUid)}" +
                $"/instances/{Uri.EscapeDataString(sopUid)}");
            Assert.Single(files);

            var retrieved = files[0];
            Assert.Equal(injectedStudyUid,
                retrieved.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));

            // Verify the frame count is preserved
            var retrFrames = retrieved.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
            Assert.Equal(origFrames, retrFrames);
        }

        [Fact]
        public async Task RealFile_QidoAfterStow_FindsByModality()
        {
            // STOW GH064.dcm (MR) and one of the GH178 CT files.
            // Then QIDO for Modality=MR should return only the MR study.
            var mrBytes = LoadTestFile("GH064.dcm");
            var ctBytes = LoadTestFile("GH178.dcm");

            using var msMr = new MemoryStream(mrBytes);
            var mrFile   = await DicomFile.OpenAsync(msMr);
            var mrStudy  = mrFile.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);

            using var msCt = new MemoryStream(ctBytes);
            var ctFile   = await DicomFile.OpenAsync(msCt);
            var ctStudy  = ctFile.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);

            // Store both
            await StowAsync(MakeStowContent(mrBytes));
            await StowAsync(MakeStowContent(ctBytes));

            // Query for series by Modality=MR within the MR study
            var mrSeriesResults = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(mrStudy)}/series?00080060=MR");
            Assert.NotEmpty(mrSeriesResults);
            Assert.All(mrSeriesResults, r =>
                Assert.Equal("MR", GetTag(r, DicomTag.Modality)));

            // CT study must NOT appear in MR modality series results
            var ctSeriesWithMrFilter = await QidoAsync(
                $"/dicomweb/studies/{Uri.EscapeDataString(ctStudy)}/series?00080060=MR");
            Assert.Empty(ctSeriesWithMrFilter);
        }

        // ── QIDO date range filtering ─────────────────────────────────────────

        [Fact]
        public async Task Qido_StudyDateRange_BothEnds_ReturnsOnlyMatchingStudies()
        {
            // Three studies with distinct dates; UIDs must be digits-and-dots only (UI VR)
            var uid1 = Uid("70.1.1");
            var uid2 = Uid("70.1.2");
            var uid3 = Uid("70.1.3");

            await StowAsync(MakeStowContent(MakeDicomBytes(uid1, Uid("70.2.1"), Uid("70.3.1"), studyDate: "20250101")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid2, Uid("70.2.2"), Uid("70.3.2"), studyDate: "20250115")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid3, Uid("70.2.3"), Uid("70.3.3"), studyDate: "20250201")));

            // Range: 20250110 – 20250120  → only uid2 (20250115)
            var results = await QidoAsync("/dicomweb/studies?00080020=20250110-20250120");

            Assert.Single(results);
            Assert.Equal(uid2, GetTag(results[0], DicomTag.StudyInstanceUID));
        }

        [Fact]
        public async Task Qido_StudyDateRange_OpenEnd_ReturnsFromDateOnward()
        {
            var uid1 = Uid("71.1.1");
            var uid2 = Uid("71.1.2");
            var uid3 = Uid("71.1.3");

            await StowAsync(MakeStowContent(MakeDicomBytes(uid1, Uid("71.2.1"), Uid("71.3.1"), studyDate: "20250101")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid2, Uid("71.2.2"), Uid("71.3.2"), studyDate: "20250115")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid3, Uid("71.2.3"), Uid("71.3.3"), studyDate: "20250201")));

            // Open end: 20250115-  → uid2 and uid3
            var results = await QidoAsync("/dicomweb/studies?00080020=20250115-");

            var uids = results.Select(r => GetTag(r, DicomTag.StudyInstanceUID)).ToList();
            Assert.Contains(uid2, uids);
            Assert.Contains(uid3, uids);
            Assert.DoesNotContain(uid1, uids);
        }

        [Fact]
        public async Task Qido_StudyDateRange_OpenStart_ReturnsUpToDate()
        {
            var uid1 = Uid("72.1.1");
            var uid2 = Uid("72.1.2");
            var uid3 = Uid("72.1.3");

            await StowAsync(MakeStowContent(MakeDicomBytes(uid1, Uid("72.2.1"), Uid("72.3.1"), studyDate: "20250101")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid2, Uid("72.2.2"), Uid("72.3.2"), studyDate: "20250115")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid3, Uid("72.2.3"), Uid("72.3.3"), studyDate: "20250201")));

            // Open start: -20250115  → uid1 and uid2
            var results = await QidoAsync("/dicomweb/studies?00080020=-20250115");

            var uids = results.Select(r => GetTag(r, DicomTag.StudyInstanceUID)).ToList();
            Assert.Contains(uid1, uids);
            Assert.Contains(uid2, uids);
            Assert.DoesNotContain(uid3, uids);
        }

        [Fact]
        public async Task Qido_StudyDateExact_ReturnsOnlyExactMatch()
        {
            var uid1 = Uid("73.1.1");
            var uid2 = Uid("73.1.2");

            await StowAsync(MakeStowContent(MakeDicomBytes(uid1, Uid("73.2.1"), Uid("73.3.1"), studyDate: "20250115")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid2, Uid("73.2.2"), Uid("73.3.2"), studyDate: "20250116")));

            var results = await QidoAsync("/dicomweb/studies?00080020=20250115");

            Assert.Single(results);
            Assert.Equal(uid1, GetTag(results[0], DicomTag.StudyInstanceUID));
        }

        [Fact]
        public async Task Qido_StudyDateRange_WithIncludefieldOnSameTag_ReturnsOnlyMatchingStudies()
        {
            // Regression: MicroDicom sends both ?StudyDate=<range> AND includefield=00080020.
            // The includefield processing must NOT overwrite the match-parameter value with an
            // empty string, otherwise the date filter is silently lost and all studies are returned.
            var uid1 = Uid("74.1.1");
            var uid2 = Uid("74.1.2");
            var uid3 = Uid("74.1.3");

            await StowAsync(MakeStowContent(MakeDicomBytes(uid1, Uid("74.2.1"), Uid("74.3.1"), studyDate: "20250101")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid2, Uid("74.2.2"), Uid("74.3.2"), studyDate: "20250115")));
            await StowAsync(MakeStowContent(MakeDicomBytes(uid3, Uid("74.2.3"), Uid("74.3.3"), studyDate: "20250201")));

            // Range: 20250110-20250120 → only uid2 (20250115).
            // includefield=00080020 mirrors MicroDicom's real request pattern.
            var results = await QidoAsync(
                "/dicomweb/studies?StudyDate=20250110-20250120&includefield=00080020&includefield=0020000D");

            Assert.Single(results);
            Assert.Equal(uid2, GetTag(results[0], DicomTag.StudyInstanceUID));
        }

        // ── Private assertion helpers ─────────────────────────────────────────

        private static void AssertTagMatches(
            DicomDataset expected, DicomDataset actual, DicomTag tag)
        {
            var exp = expected.GetSingleValueOrDefault(tag, string.Empty);
            var act = actual.GetSingleValueOrDefault(tag, string.Empty);
            Assert.Equal(exp, act);
        }
    }
}
