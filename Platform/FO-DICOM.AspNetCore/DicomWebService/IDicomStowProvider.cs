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
    /// STOW-RS Store Transaction requests (PS3.18 Section 10.5).
    /// <para>
    /// The framework parses the <c>multipart/related; type="application/dicom"</c> request body,
    /// enforces Study Instance UID scope validation when the request targets
    /// <c>POST .../studies/{studyInstanceUID}</c>, and passes the validated instances to
    /// <see cref="OnStoreInstancesAsync"/>. Instances that fail UID validation are excluded
    /// from the request and reported as failures in the response automatically.
    /// </para>
    /// <para>
    /// If a multipart part cannot be parsed as a valid DICOM file (and therefore has no SOP
    /// Instance UID), the entire request is rejected with HTTP 400 Bad Request before
    /// this method is called.
    /// </para>
    /// </summary>
    public interface IDicomStowProvider
    {
        /// <summary>
        /// Stores the DICOM instances supplied in <paramref name="request"/>.
        /// </summary>
        /// <param name="request">
        /// The parsed STOW-RS request. <see cref="DicomStowRequest.Instances"/> contains only
        /// the instances that passed Study Instance UID validation. Instances that failed
        /// validation are excluded and will be reported as failures automatically by the framework.
        /// </param>
        /// <param name="httpContext">
        /// The full ASP.NET Core HTTP context. Use this to inspect authentication, request
        /// headers, or resolve scoped services via <c>httpContext.RequestServices</c>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token tied to the HTTP request lifetime.</param>
        /// <returns>
        /// One of:
        /// <list type="bullet">
        ///   <item>
        ///     <see cref="DicomStowSuccessResponse"/> — all instances were stored (HTTP 200).
        ///   </item>
        ///   <item>
        ///     <see cref="DicomStowPartialSuccessResponse"/> — some instances were stored,
        ///     some failed (HTTP 202). The framework merges any framework-level failures
        ///     (UID mismatches) into the failed list before writing the response.
        ///   </item>
        ///   <item>
        ///     Any <see cref="DicomWebFailureResponse"/> subclass — e.g.
        ///     <see cref="DicomWebBadRequestResponse"/>, <see cref="DicomWebForbiddenResponse"/>,
        ///     <see cref="DicomWebUnavailableResponse"/> (HTTP 400/403/503).
        ///   </item>
        /// </list>
        /// </returns>
        Task<IDicomStowResponse> OnStoreInstancesAsync(
            DicomStowRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken);
    }
}
