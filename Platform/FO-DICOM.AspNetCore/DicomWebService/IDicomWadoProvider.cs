// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Implemented by a concrete <see cref="DicomWebService"/> subclass to handle
    /// WADO-RS Retrieve Transaction requests (PS3.18 Section 10.4).
    /// <para>
    /// Implement this interface alongside <see cref="IDicomQidoProvider"/> when the service
    /// supports both QIDO-RS search and WADO-RS retrieval. If only one is implemented, the
    /// framework returns HTTP 501 for the unsupported transaction type.
    /// </para>
    /// </summary>
    public interface IDicomWadoProvider
    {
        /// <summary>
        /// Retrieves DICOM instances for the given study, series, or instance scope
        /// (PS3.18 Section 10.4.1.1.1).
        /// <para>
        /// The <paramref name="request"/> carries the UIDs that identify the scope:
        /// only <see cref="DicomWadoRequest.StudyInstanceUid"/> is set for study-level retrieval;
        /// both Study and <see cref="DicomWadoRequest.SeriesInstanceUid"/> are set for series-level;
        /// all three UIDs are set for single-instance retrieval.
        /// </para>
        /// </summary>
        /// <returns>
        /// One of:
        /// <list type="bullet">
        ///   <item><see cref="DicomWadoInstancesResponse"/> — list of <see cref="DicomFile"/> objects</item>
        ///   <item><see cref="DicomWadoRawInstancesResponse"/> — list of pre-encoded Part 10 streams</item>
        ///   <item><see cref="DicomWadoAsyncInstancesResponse"/> — async-enumerable of <see cref="DicomFile"/></item>
        ///   <item><see cref="DicomWadoAsyncRawInstancesResponse"/> — async-enumerable of pre-encoded streams</item>
        ///   <item>Any <see cref="DicomWebFailureResponse"/> subclass (400, 401, 403, 404, 501, 503)</item>
        /// </list>
        /// </returns>
        Task<IDicomWadoResponse> OnRetrieveInstancesAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves instance metadata (DICOM datasets without bulk data) for the given scope
        /// (PS3.18 Section 10.4.1.1.2).
        /// <para>
        /// Return <see cref="DicomDataset"/> objects with bulk data attributes removed or replaced
        /// with bulk data URI references. The framework serializes them as
        /// <c>application/dicom+json</c> (default) or
        /// <c>multipart/related; type="application/dicom+xml"</c>.
        /// </para>
        /// </summary>
        /// <returns>
        /// One of:
        /// <list type="bullet">
        ///   <item><see cref="DicomWadoMetadataResponse"/> — list of <see cref="DicomDataset"/></item>
        ///   <item><see cref="DicomWadoAsyncMetadataResponse"/> — async-enumerable of <see cref="DicomDataset"/></item>
        ///   <item>Any <see cref="DicomWebFailureResponse"/> subclass (400, 401, 403, 404, 501, 503)</item>
        /// </list>
        /// </returns>
        Task<IDicomWadoResponse> OnRetrieveMetadataAsync(DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    }
}
