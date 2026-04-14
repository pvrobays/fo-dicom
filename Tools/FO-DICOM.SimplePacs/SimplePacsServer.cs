// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.SimplePacs.Data;
using FellowOakDicom.SimplePacs.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.SimplePacs
{
    /// <summary>
    /// Simple PACS DICOMweb server.
    /// Stores received DICOM files in a folder hierarchy under a configurable root directory
    /// and indexes metadata in a SQLite database for QIDO-RS queries.
    /// Implements STOW-RS, QIDO-RS, and WADO-RS (PS3.18).
    /// </summary>
    public class SimplePacsServer : DicomWebService,
        IDicomStowProvider,
        IDicomQidoProvider,
        IDicomWadoProvider
    {
        private readonly IDbContextFactory<SimplePacsDbContext> _dbFactory;
        private readonly DicomFileStore _fileStore;
        private readonly ILogger _logger;

        public SimplePacsServer(
            IDbContextFactory<SimplePacsDbContext> dbFactory,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
            _dbFactory = dbFactory;
            _logger = loggerFactory.CreateLogger<SimplePacsServer>();
            var storageRoot = configuration["SimplePacs:StorageRoot"] ?? "dicom-storage";
            _fileStore = new DicomFileStore(storageRoot);
        }

        // ── STOW-RS ───────────────────────────────────────────────────────────

        public async Task<IDicomStowResponse> OnStoreInstancesAsync(
            DicomStowRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var stored = new List<DicomStowInstanceResult>();
            var failed = new List<DicomStowInstanceResult>();

            foreach (var file in request.Instances)
            {
                var ds = file.Dataset;
                var studyUid  = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID,  (string?)null);
                var seriesUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, (string?)null);
                var sopUid    = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID,    (string?)null);
                var sopClass  = ds.GetSingleValueOrDefault(DicomTag.SOPClassUID,       (string?)null);

                if (string.IsNullOrEmpty(studyUid) || string.IsNullOrEmpty(seriesUid) || string.IsNullOrEmpty(sopUid))
                {
                    failed.Add(new DicomStowInstanceResult(
                        sopClass ?? string.Empty,
                        sopUid ?? string.Empty,
                        0x0110)); // Processing failure — missing required UIDs
                    continue;
                }

                var fileSaved = false;
                try
                {
                    // ── Upsert study ─────────────────────────────────────────
                    var study = await db.Studies
                        .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid, cancellationToken);
                    if (study == null)
                    {
                        study = new StudyRecord { StudyInstanceUid = studyUid };
                        db.Studies.Add(study);
                        await db.SaveChangesAsync(cancellationToken); // get Id
                    }
                    // Always refresh study-level tags from incoming data
                    study.StudyDate             = ds.GetSingleValueOrDefault(DicomTag.StudyDate,             (string?)null);
                    study.StudyTime             = ds.GetSingleValueOrDefault(DicomTag.StudyTime,             (string?)null);
                    study.AccessionNumber       = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber,       (string?)null);
                    study.ReferringPhysicianName = ds.GetSingleValueOrDefault(DicomTag.ReferringPhysicianName, (string?)null);
                    study.PatientName           = ds.GetSingleValueOrDefault(DicomTag.PatientName,           (string?)null);
                    study.PatientId             = ds.GetSingleValueOrDefault(DicomTag.PatientID,             (string?)null);
                    study.PatientBirthDate      = ds.GetSingleValueOrDefault(DicomTag.PatientBirthDate,      (string?)null);
                    study.PatientSex            = ds.GetSingleValueOrDefault(DicomTag.PatientSex,            (string?)null);
                    study.StudyId               = ds.GetSingleValueOrDefault(DicomTag.StudyID,               (string?)null);

                    // ── Upsert series ─────────────────────────────────────────
                    var series = await db.Series
                        .FirstOrDefaultAsync(s => s.SeriesInstanceUid == seriesUid, cancellationToken);
                    if (series == null)
                    {
                        series = new SeriesRecord { StudyId = study.Id, SeriesInstanceUid = seriesUid };
                        db.Series.Add(series);
                        await db.SaveChangesAsync(cancellationToken); // get Id
                    }
                    series.Modality           = ds.GetSingleValueOrDefault(DicomTag.Modality,          (string?)null);
                    series.SeriesDescription  = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, (string?)null);
                    series.SeriesNumber       = ds.TryGetSingleValue(DicomTag.SeriesNumber, out int sn) ? sn : (int?)null;

                    // ── Upsert instance ───────────────────────────────────────
                    var instance = await db.Instances
                        .FirstOrDefaultAsync(i => i.SopInstanceUid == sopUid, cancellationToken);
                    if (instance == null)
                    {
                        instance = new InstanceRecord { SeriesId = series.Id, SopInstanceUid = sopUid };
                        db.Instances.Add(instance);
                    }
                    instance.SopClassUid      = sopClass;
                    instance.InstanceNumber   = ds.TryGetSingleValue(DicomTag.InstanceNumber, out int instNum) ? instNum : (int?)null;
                    instance.TransferSyntaxUid = file.FileMetaInfo?.TransferSyntax?.UID.UID;
                    instance.FilePath         = $"{studyUid}/{sopUid}.dcm";

                    // ── Save file to disk ─────────────────────────────────────
                    // fileSaved is declared before the try so the catch can clean up
                    // if SaveChangesAsync throws after the file has been written.
                    await _fileStore.SaveAsync(file, studyUid, sopUid, cancellationToken);
                    fileSaved = true;

                    await db.SaveChangesAsync(cancellationToken);

                    stored.Add(new DicomStowInstanceResult(sopClass ?? string.Empty, sopUid));
                }
                catch (Exception)
                {
                    // If the file reached disk but the DB commit failed, delete it so
                    // we don't accumulate orphaned files.
                    if (fileSaved)
                        try { _fileStore.Delete(studyUid, sopUid); } catch { /* best-effort */ }
                    // Detach all tracked entities so the next iteration starts with a
                    // clean change tracker — avoids cascading save failures.
                    db.ChangeTracker.Clear();
                    failed.Add(new DicomStowInstanceResult(
                        sopClass ?? string.Empty, sopUid, 0x0110)); // Processing failure
                    continue;
                }
            }

            // Recompute derived study counts after all instances are processed.
            // Wrapped in try/catch — a failure here is non-fatal; counts will be corrected
            // on the next successful STOW for the same study.
            try
            {
                await RecomputeStudyCountsAsync(_dbFactory, request.Instances, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recompute study counts after STOW — counts may be stale");
            }

            if (failed.Count == 0)
                return new DicomStowSuccessResponse(stored);

            return new DicomStowPartialSuccessResponse(stored, failed);
        }

        /// <summary>
        /// Recomputes NumberOfStudyRelatedSeries, NumberOfStudyRelatedInstances,
        /// and ModalitiesInStudy for every study touched by the given instances.
        /// Uses a fresh DbContext to avoid EF Core change-tracker identity-map
        /// interfering with navigation-property loading on already-tracked entities.
        /// </summary>
        private static async Task RecomputeStudyCountsAsync(
            IDbContextFactory<SimplePacsDbContext> dbFactory,
            IReadOnlyList<DicomFile> instances,
            CancellationToken cancellationToken)
        {
            // Collect the distinct study UIDs from the submitted instances.
            var studyUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in instances)
            {
                var uid = f.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, (string?)null);
                if (!string.IsNullOrEmpty(uid)) studyUids.Add(uid);
            }

            // Use a fresh context so the query is not affected by the change-tracker
            // state of the STOW context (navigation properties may not be fully loaded).
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            foreach (var studyUid in studyUids)
            {
                var study = await db.Studies
                    .Include(s => s.Series)
                    .ThenInclude(sr => sr.Instances)
                    .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid, cancellationToken);
                if (study == null) continue;

                study.NumberOfStudyRelatedSeries    = study.Series.Count;
                study.NumberOfStudyRelatedInstances = study.Series.Sum(sr => sr.Instances.Count);

                var modalities = study.Series
                    .Select(sr => sr.Modality)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();
                study.ModalitiesInStudy = modalities.Count > 0
                    ? string.Join("\\", modalities)
                    : null;

                foreach (var s in study.Series)
                    s.NumberOfSeriesRelatedInstances = s.Instances.Count;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        // ── QIDO-RS ───────────────────────────────────────────────────────────

        public async Task<IDicomQidoResponse> OnQidoRequestAsync(
            DicomQidoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var response = new DicomQidoSuccessResponse { IsFuzzyMatchingSupported = false };

            switch (request.Level)
            {
                case Network.DicomQueryRetrieveLevel.Study:
                    await QueryStudiesAsync(db, request, response, cancellationToken);
                    break;
                case Network.DicomQueryRetrieveLevel.Series:
                    await QuerySeriesAsync(db, request, response, cancellationToken);
                    break;
                case Network.DicomQueryRetrieveLevel.Image:
                    await QueryInstancesAsync(db, request, response, cancellationToken);
                    break;
            }

            return response;
        }

        // ── QIDO study query ──────────────────────────────────────────────────

        private static async Task QueryStudiesAsync(
            SimplePacsDbContext db,
            DicomQidoRequest request,
            DicomQidoSuccessResponse response,
            CancellationToken cancellationToken)
        {
            var ds = request.Dataset;
            IQueryable<StudyRecord> q = db.Studies;

            // Study Instance UID filter (exact or list)
            var studyUidFilter = GetFilterValue(ds, DicomTag.StudyInstanceUID);
            if (!string.IsNullOrEmpty(studyUidFilter))
                q = ApplyUidFilter(q, r => r.StudyInstanceUid, studyUidFilter);

            // Patient ID filter
            var patientIdFilter = GetFilterValue(ds, DicomTag.PatientID);
            if (!string.IsNullOrEmpty(patientIdFilter))
                q = ApplyStringFilter(q, r => r.PatientId, patientIdFilter);

            // Patient Name filter (wildcard supported)
            var patientNameFilter = GetFilterValue(ds, DicomTag.PatientName);
            if (!string.IsNullOrEmpty(patientNameFilter))
                q = ApplyStringFilter(q, r => r.PatientName, patientNameFilter);

            // Accession Number filter
            var accessionFilter = GetFilterValue(ds, DicomTag.AccessionNumber);
            if (!string.IsNullOrEmpty(accessionFilter))
                q = ApplyStringFilter(q, r => r.AccessionNumber, accessionFilter);

            // Study Date range filter
            var studyDateFilter = GetFilterValue(ds, DicomTag.StudyDate);
            if (!string.IsNullOrEmpty(studyDateFilter))
                q = ApplyDateFilter(q, r => r.StudyDate, studyDateFilter);

            // Modalities In Study filter — delimiter-aware to avoid "MR" matching "MRI".
            // ModalitiesInStudy is stored as backslash-separated tokens e.g. "CT\MR\PT".
            // We match when the token appears as the whole value, at the start, at the end,
            // or in the middle — all delimited by backslashes.
            var modalityFilter = GetFilterValue(ds, DicomTag.ModalitiesInStudy);
            if (!string.IsNullOrEmpty(modalityFilter))
                q = q.Where(r => r.ModalitiesInStudy != null && (
                    r.ModalitiesInStudy == modalityFilter ||
                    EF.Functions.Like(r.ModalitiesInStudy, modalityFilter + @"\%") ||
                    EF.Functions.Like(r.ModalitiesInStudy, @"%\" + modalityFilter) ||
                    EF.Functions.Like(r.ModalitiesInStudy, @"%\" + modalityFilter + @"\%")));

            // Pagination
            if (request.Options.Offset > 0) q = q.Skip(request.Options.Offset);
            if (request.Options.Limit > 0)  q = q.Take(request.Options.Limit);

            var results = await q.ToListAsync(cancellationToken);
            foreach (var row in results)
                response.AddResult(BuildStudyDataset(row));
        }

        private static DicomDataset BuildStudyDataset(StudyRecord row)
        {
            var result = new DicomDataset().NotValidated();
            result.Add(DicomTag.StudyInstanceUID,              row.StudyInstanceUid);
            result.Add(DicomTag.StudyDate,                     row.StudyDate ?? string.Empty);
            result.Add(DicomTag.StudyTime,                     row.StudyTime ?? string.Empty);
            result.Add(DicomTag.AccessionNumber,               row.AccessionNumber ?? string.Empty);
            result.Add(DicomTag.ReferringPhysicianName,        row.ReferringPhysicianName ?? string.Empty);
            result.Add(DicomTag.PatientName,                   row.PatientName ?? string.Empty);
            result.Add(DicomTag.PatientID,                     row.PatientId ?? string.Empty);
            result.Add(DicomTag.PatientBirthDate,              row.PatientBirthDate ?? string.Empty);
            result.Add(DicomTag.PatientSex,                    row.PatientSex ?? string.Empty);
            result.Add(DicomTag.StudyID,                       row.StudyId ?? string.Empty);
            result.Add(DicomTag.ModalitiesInStudy,             row.ModalitiesInStudy ?? string.Empty);
            result.Add(DicomTag.InstanceAvailability,          "ONLINE");
            result.Add(DicomTag.NumberOfStudyRelatedSeries,    row.NumberOfStudyRelatedSeries.ToString());
            result.Add(DicomTag.NumberOfStudyRelatedInstances, row.NumberOfStudyRelatedInstances.ToString());
            return result;
        }

        // ── QIDO series query ─────────────────────────────────────────────────

        private static async Task QuerySeriesAsync(
            SimplePacsDbContext db,
            DicomQidoRequest request,
            DicomQidoSuccessResponse response,
            CancellationToken cancellationToken)
        {
            var ds = request.Dataset;
            IQueryable<SeriesRecord> q = db.Series;

            // Study Instance UID scope (from route or query param)
            var studyUidFilter = GetFilterValue(ds, DicomTag.StudyInstanceUID);
            if (!string.IsNullOrEmpty(studyUidFilter))
                q = q.Where(r => r.Study != null && r.Study.StudyInstanceUid == studyUidFilter);

            // Series Instance UID filter
            var seriesUidFilter = GetFilterValue(ds, DicomTag.SeriesInstanceUID);
            if (!string.IsNullOrEmpty(seriesUidFilter))
                q = ApplyUidFilter(q, r => r.SeriesInstanceUid, seriesUidFilter);

            // Modality filter
            var modalityFilter = GetFilterValue(ds, DicomTag.Modality);
            if (!string.IsNullOrEmpty(modalityFilter))
                q = ApplyStringFilter(q, r => r.Modality, modalityFilter);

            // Pagination
            if (request.Options.Offset > 0) q = q.Skip(request.Options.Offset);
            if (request.Options.Limit > 0)  q = q.Take(request.Options.Limit);

            var results = await q.Include(r => r.Study).ToListAsync(cancellationToken);
            foreach (var row in results)
                response.AddResult(BuildSeriesDataset(row));
        }

        private static DicomDataset BuildSeriesDataset(SeriesRecord row)
        {
            var result = new DicomDataset().NotValidated();
            if (row.Study != null)
                result.Add(DicomTag.StudyInstanceUID, row.Study.StudyInstanceUid);
            result.Add(DicomTag.SeriesInstanceUID,              row.SeriesInstanceUid);
            result.Add(DicomTag.Modality,                       row.Modality ?? string.Empty);
            result.Add(DicomTag.SeriesDescription,              row.SeriesDescription ?? string.Empty);
            result.Add(DicomTag.SeriesNumber,                   row.SeriesNumber?.ToString() ?? string.Empty);
            result.Add(DicomTag.InstanceAvailability,           "ONLINE");
            result.Add(DicomTag.NumberOfSeriesRelatedInstances, row.NumberOfSeriesRelatedInstances.ToString());
            return result;
        }

        // ── QIDO instance query ───────────────────────────────────────────────

        private static async Task QueryInstancesAsync(
            SimplePacsDbContext db,
            DicomQidoRequest request,
            DicomQidoSuccessResponse response,
            CancellationToken cancellationToken)
        {
            var ds = request.Dataset;
            IQueryable<InstanceRecord> q = db.Instances;

            // Study Instance UID scope
            var studyUidFilter = GetFilterValue(ds, DicomTag.StudyInstanceUID);
            if (!string.IsNullOrEmpty(studyUidFilter))
                q = q.Where(r => r.Series != null && r.Series.Study != null &&
                                 r.Series.Study.StudyInstanceUid == studyUidFilter);

            // Series Instance UID scope
            var seriesUidFilter = GetFilterValue(ds, DicomTag.SeriesInstanceUID);
            if (!string.IsNullOrEmpty(seriesUidFilter))
                q = q.Where(r => r.Series != null && r.Series.SeriesInstanceUid == seriesUidFilter);

            // SOP Instance UID filter
            var sopUidFilter = GetFilterValue(ds, DicomTag.SOPInstanceUID);
            if (!string.IsNullOrEmpty(sopUidFilter))
                q = ApplyUidFilter(q, r => r.SopInstanceUid, sopUidFilter);

            // SOP Class UID filter
            var sopClassFilter = GetFilterValue(ds, DicomTag.SOPClassUID);
            if (!string.IsNullOrEmpty(sopClassFilter))
                q = ApplyUidFilter(q, r => r.SopClassUid, sopClassFilter);

            // Pagination
            if (request.Options.Offset > 0) q = q.Skip(request.Options.Offset);
            if (request.Options.Limit > 0)  q = q.Take(request.Options.Limit);

            var results = await q
                .Include(r => r.Series)
                .ThenInclude(s => s!.Study)
                .ToListAsync(cancellationToken);

            foreach (var row in results)
                response.AddResult(BuildInstanceDataset(row));
        }

        private static DicomDataset BuildInstanceDataset(InstanceRecord row)
        {
            var result = new DicomDataset().NotValidated();
            if (row.Series?.Study != null)
                result.Add(DicomTag.StudyInstanceUID,  row.Series.Study.StudyInstanceUid);
            if (row.Series != null)
                result.Add(DicomTag.SeriesInstanceUID, row.Series.SeriesInstanceUid);
            result.Add(DicomTag.SOPInstanceUID,        row.SopInstanceUid);
            result.Add(DicomTag.SOPClassUID,           row.SopClassUid ?? string.Empty);
            result.Add(DicomTag.InstanceNumber,        row.InstanceNumber?.ToString() ?? string.Empty);
            result.Add(DicomTag.InstanceAvailability,  "ONLINE");
            return result;
        }

        // ── QIDO filter helpers ───────────────────────────────────────────────

        /// <summary>
        /// Returns the filter string value for a tag in the QIDO request dataset,
        /// or <c>null</c> when the tag is absent or its value is empty (include-only).
        /// </summary>
        private static string? GetFilterValue(DicomDataset ds, DicomTag tag)
        {
            if (!ds.Contains(tag)) return null;
            var val = ds.GetSingleValueOrDefault(tag, (string?)null);
            return string.IsNullOrEmpty(val) ? null : val;
        }

        /// <summary>
        /// Applies an exact-match or wildcard (DICOM '*' → SQL LIKE '%') string filter.
        /// Comparisons are case-insensitive via SQLite LIKE semantics.
        /// </summary>
        private static IQueryable<T> ApplyStringFilter<T>(
            IQueryable<T> q,
            System.Linq.Expressions.Expression<Func<T, string?>> selector,
            string filterValue)
        {
            if (filterValue.Contains('*') || filterValue.Contains('?'))
            {
                // Convert DICOM wildcard to SQL LIKE pattern.
                var pattern = filterValue
                    .Replace("%", "\\%")
                    .Replace("_", "\\_")
                    .Replace("*", "%")
                    .Replace("?", "_");
                return q.Where(BuildLikePredicate<T>(selector, pattern));
            }
            // Exact match (case-insensitive via EF Core SQLite LIKE).
            return q.Where(BuildEqualsPredicate<T>(selector, filterValue));
        }

        /// <summary>Applies a comma-separated UID list filter (OR semantics).</summary>
        private static IQueryable<T> ApplyUidFilter<T>(
            IQueryable<T> q,
            System.Linq.Expressions.Expression<Func<T, string?>> selector,
            string filterValue)
        {
            var uids = filterValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (uids.Length == 1)
                return q.Where(BuildEqualsPredicate<T>(selector, uids[0].Trim()));

            var trimmed = uids.Select(u => u.Trim()).ToList();
            // EF Core translates Contains to SQL IN.
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
            var memberExpr = System.Linq.Expressions.Expression.Invoke(selector, param);
            var inExpr = System.Linq.Expressions.Expression.Call(
                System.Linq.Expressions.Expression.Constant(trimmed),
                typeof(List<string>).GetMethod("Contains", new[] { typeof(string) })!,
                memberExpr);
            var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(inExpr, param);
            return q.Where(lambda);
        }

        /// <summary>
        /// Applies a DICOM date-range filter. Supports "YYYYMMDD", "YYYYMMDD-", "-YYYYMMDD",
        /// and "YYYYMMDD-YYYYMMDD" formats.
        /// </summary>
        private static IQueryable<T> ApplyDateFilter<T>(
            IQueryable<T> q,
            System.Linq.Expressions.Expression<Func<T, string?>> selector,
            string filterValue)
        {
            if (filterValue.Contains('-'))
            {
                var parts = filterValue.Split('-');
                var from = parts[0];
                var to   = parts.Length > 1 ? parts[1] : string.Empty;

                if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                {
                    // Both ends: date >= from AND date <= to
                    q = q.Where(BuildComparePredicate<T>(selector, from, ">="));
                    q = q.Where(BuildComparePredicate<T>(selector, to,   "<="));
                }
                else if (!string.IsNullOrEmpty(from))
                {
                    q = q.Where(BuildComparePredicate<T>(selector, from, ">="));
                }
                else if (!string.IsNullOrEmpty(to))
                {
                    q = q.Where(BuildComparePredicate<T>(selector, to, "<="));
                }
            }
            else
            {
                // Exact date match
                q = q.Where(BuildEqualsPredicate<T>(selector, filterValue));
            }
            return q;
        }

        // Expression builders (needed because EF Core can't translate arbitrary lambda closures)

        private static System.Linq.Expressions.Expression<Func<T, bool>> BuildEqualsPredicate<T>(
            System.Linq.Expressions.Expression<Func<T, string?>> selector, string value)
        {
            // Simple equality — no ToUpper() (which EF Core SQLite cannot translate).
            // DICOM UIDs and dates are case-sensitive; patient names are stored as received.
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
            var member = System.Linq.Expressions.Expression.Invoke(selector, param);
            var constant = System.Linq.Expressions.Expression.Constant(value, typeof(string));
            var equals = System.Linq.Expressions.Expression.Equal(member, constant);
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(equals, param);
        }

        private static System.Linq.Expressions.Expression<Func<T, bool>> BuildLikePredicate<T>(
            System.Linq.Expressions.Expression<Func<T, string?>> selector, string pattern)
        {
            // Use EF.Functions.Like directly — no ToUpper() (untranslatable).
            // SQLite LIKE is case-insensitive for ASCII by default, which covers
            // the DICOM wildcard use-case adequately.
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
            var member = System.Linq.Expressions.Expression.Invoke(selector, param);
            var efFunctions = System.Linq.Expressions.Expression.Constant(EF.Functions);
            var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
                "Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;
            var likeCall = System.Linq.Expressions.Expression.Call(
                likeMethod, efFunctions, member,
                System.Linq.Expressions.Expression.Constant(pattern));
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(likeCall, param);
        }

        private static System.Linq.Expressions.Expression<Func<T, bool>> BuildComparePredicate<T>(
            System.Linq.Expressions.Expression<Func<T, string?>> selector,
            string value,
            string op)
        {
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
            var member = System.Linq.Expressions.Expression.Invoke(selector, param);
            var constant = System.Linq.Expressions.Expression.Constant(value, typeof(string));
            // Use the 2-parameter string.Compare overload — EF Core SQLite CAN translate this,
            // unlike the 3-parameter overload that takes StringComparison.
            // DA strings are YYYYMMDD so lexicographic ordering == chronological ordering.
            var compareCall = System.Linq.Expressions.Expression.Call(
                typeof(string).GetMethod("Compare", new[] { typeof(string), typeof(string) })!,
                member, constant);
            System.Linq.Expressions.Expression comparison = op == ">="
                ? System.Linq.Expressions.Expression.GreaterThanOrEqual(compareCall,
                    System.Linq.Expressions.Expression.Constant(0))
                : System.Linq.Expressions.Expression.LessThanOrEqual(compareCall,
                    System.Linq.Expressions.Expression.Constant(0));
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(comparison, param);
        }

        // ── WADO-RS ───────────────────────────────────────────────────────────

        public async Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
            DicomWadoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var instances = await GetMatchingInstancesAsync(db, request, cancellationToken);

            if (instances.Count == 0)
                return new DicomWebNotFoundResponse();

            // Load each file fully into memory so the underlying FileStream is closed
            // before we return, preventing file-locking issues during cleanup.
            var fileList = new List<DicomFile>();
            foreach (var inst in instances)
            {
                var studyUid = inst.Series?.Study?.StudyInstanceUid;
                if (studyUid == null) continue;
                var file = await _fileStore.LoadAsync(studyUid, inst.SopInstanceUid, cancellationToken);
                if (file == null) continue;
                fileList.Add(file);
            }

            if (fileList.Count == 0)
                return new DicomWebNotFoundResponse();

            return new DicomWadoInstancesResponse(fileList);
        }

        public async Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
            DicomWadoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var instances = await GetMatchingInstancesAsync(db, request, cancellationToken);

            if (instances.Count == 0)
                return new DicomWebNotFoundResponse();

            var datasets = new List<DicomDataset>();
            foreach (var inst in instances)
            {
                var studyUid = inst.Series?.Study?.StudyInstanceUid;
                if (studyUid == null) continue;
                var file = await _fileStore.LoadAsync(studyUid, inst.SopInstanceUid, cancellationToken);
                if (file == null) continue;
                datasets.Add(file.Dataset);
            }

            if (datasets.Count == 0)
                return new DicomWebNotFoundResponse();

            return new DicomWadoMetadataResponse(datasets);
        }

        /// <summary>
        /// Queries the DB for instances matching the study/series/sop UID scope in the request.
        /// Always eagerly loads Series → Study navigation properties.
        /// </summary>
        private static async Task<List<InstanceRecord>> GetMatchingInstancesAsync(
            SimplePacsDbContext db,
            DicomWadoRequest request,
            CancellationToken cancellationToken)
        {
            IQueryable<InstanceRecord> q = db.Instances
                .Include(i => i.Series)
                .ThenInclude(s => s!.Study);

            q = q.Where(i => i.Series != null &&
                             i.Series.Study != null &&
                             i.Series.Study.StudyInstanceUid == request.StudyInstanceUid);

            if (request.SeriesInstanceUid != null)
                q = q.Where(i => i.Series != null &&
                                 i.Series.SeriesInstanceUid == request.SeriesInstanceUid);

            if (request.SopInstanceUid != null)
                q = q.Where(i => i.SopInstanceUid == request.SopInstanceUid);

            return await q.ToListAsync(cancellationToken);
        }
    }
}
