// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Defines the contract for handling DICOMweb requests (QIDO-RS and WADO-RS).
    /// Implemented by <see cref="DicomWebService"/>.
    /// </summary>
    public interface IDicomWebService
    {
        // ── QIDO-RS (Search) ───────────────────────────────────────────────────

        /// <summary>Handles a QIDO-RS All Studies search (<c>GET …/studies</c>).</summary>
        Task HandleQidoStudiesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Series search.
        /// <list type="bullet">
        ///   <item><c>GET …/series</c> — all series (no route values needed)</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series</c> — series within a study</item>
        /// </list>
        /// The study scope is extracted automatically from the <c>studyInstanceUID</c> route value
        /// when present in <see cref="HttpRequest.RouteValues"/>.
        /// </summary>
        Task HandleQidoSeriesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Instances search.
        /// <list type="bullet">
        ///   <item><c>GET …/instances</c> — all instances</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/instances</c> — instances within a study</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances</c> — instances within a series</item>
        /// </list>
        /// The study and series scope are extracted automatically from the route values
        /// <c>studyInstanceUID</c> and <c>seriesInstanceUID</c> when present.
        /// </summary>
        Task HandleQidoInstancesRequestAsync(HttpContext context);

        // ── WADO-RS (Retrieve Instances) ───────────────────────────────────────

        /// <summary>
        /// Handles a WADO-RS Instance Resources request (PS3.18 Section 10.4.1.1.1).
        /// Covers study-, series-, and instance-level retrieval — the scope is determined
        /// by which of <c>studyInstanceUID</c>, <c>seriesInstanceUID</c>, and
        /// <c>sopInstanceUID</c> route values are present.
        /// <list type="bullet">
        ///   <item><c>GET …/studies/{studyInstanceUID}</c></item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}</c></item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}</c></item>
        /// </list>
        /// </summary>
        Task HandleWadoInstancesRequestAsync(HttpContext context);

        // ── WADO-RS (Retrieve Metadata) ────────────────────────────────────────

        /// <summary>
        /// Handles a WADO-RS Metadata Resources request (PS3.18 Section 10.4.1.1.2).
        /// Returns instance metadata (DICOM datasets without bulk data) as JSON or XML.
        /// Covers study-, series-, and instance-level metadata — the scope is determined
        /// by which route values are present.
        /// <list type="bullet">
        ///   <item><c>GET …/studies/{studyInstanceUID}/metadata</c></item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/metadata</c></item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/metadata</c></item>
        /// </list>
        /// </summary>
        Task HandleWadoMetadataRequestAsync(HttpContext context);

        // ── WADO-RS (Retrieve Frames) ──────────────────────────────────────────

        /// <summary>
        /// Handles a WADO-RS Frame Resources request (PS3.18 Section 10.4.1.1.4).
        /// Returns raw pixel data for one or more frames of a single instance as a
        /// <c>multipart/related</c> response with per-frame MIME types.
        /// <list type="bullet">
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/frames/{frameList}</c></item>
        /// </list>
        /// The <c>{frameList}</c> is a comma-separated list of 1-based frame numbers
        /// (e.g. <c>1</c>, <c>1,3,5</c>).
        /// </summary>
        Task HandleWadoFramesRequestAsync(HttpContext context);

        // ── WADO-RS (Retrieve Bulk Data) ───────────────────────────────────────

        /// <summary>
        /// Handles a WADO-RS Bulk Data Resources request (PS3.18 Section 10.4.1.1.5).
        /// Returns the raw bytes of a single bulk data element as a
        /// <c>multipart/related; type="application/octet-stream"</c> response.
        /// <list type="bullet">
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/bulk/{**bulkPath}</c></item>
        /// </list>
        /// <para>
        /// The <c>{**bulkPath}</c> catch-all segment identifies the element within the dataset:
        /// <list type="bullet">
        ///   <item>Top-level element: <c>7FE00010</c> (8-hex-digit tag)</item>
        ///   <item>Nested element: <c>{seqTag}/{itemIndex}/{elementTag}</c>
        ///     (e.g. <c>54000100/0/54001010</c>)</item>
        /// </list>
        /// These paths match the URIs embedded in <c>"BulkDataURI"</c> fields of metadata
        /// responses.
        /// </para>
        /// </summary>
        Task HandleWadoBulkDataRequestAsync(HttpContext context);
    }
}
