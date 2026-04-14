// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Marker interface for responses to STOW-RS Store Transaction requests
    /// (<see cref="FellowOakDicom.AspNetCore.DicomWebService.IDicomStowProvider.OnStoreInstancesAsync"/>).
    /// Implemented by STOW success/partial-success response types and all
    /// <see cref="DicomWebFailureResponse"/> subclasses.
    /// </summary>
    public interface IDicomStowResponse { }

    /// <summary>
    /// Per-instance result record used in STOW-RS success and partial-success responses.
    /// Corresponds to one item in the Referenced SOP Sequence (0008,1199) or
    /// Failed SOP Sequence (0008,1198) of the STOW-RS Response Module (PS3.18 Section 10.5.1).
    /// </summary>
    public class DicomStowInstanceResult
    {
        /// <summary>
        /// The SOP Class UID of the stored (or attempted) instance.
        /// Corresponds to Referenced SOP Class UID (0008,1150).
        /// </summary>
        public string ReferencedSopClassUid { get; }

        /// <summary>
        /// The SOP Instance UID of the stored (or attempted) instance.
        /// Corresponds to Referenced SOP Instance UID (0008,1155).
        /// </summary>
        public string ReferencedSopInstanceUid { get; }

        /// <summary>
        /// The retrieve URL (WADO-RS URI) for this instance, included in the Referenced SOP
        /// Sequence for successfully stored instances. Corresponds to Retrieve URL (0008,1190).
        /// May be <c>null</c> when the retrieve URL is not available or the instance failed.
        /// </summary>
        public string? RetrieveUrl { get; }

        /// <summary>
        /// The DICOM failure reason code for a failed instance, per PS3.4 Annex CC.
        /// <c>null</c> for successfully stored instances.
        /// Common values:
        /// <list type="bullet">
        ///   <item><c>0x0110</c> — Processing failure</item>
        ///   <item><c>0x0122</c> — SOP Class not supported</item>
        ///   <item><c>0xA700</c> — Out of resources</item>
        ///   <item><c>0xC409</c> — Image type not supported</item>
        ///   <item><c>0xC996</c> — Study Instance UID mismatch (route UID does not match instance)</item>
        /// </list>
        /// </summary>
        public ushort? FailureReason { get; }

        /// <summary>
        /// Creates a successful instance result (Referenced SOP Sequence item).
        /// </summary>
        /// <param name="referencedSopClassUid">SOP Class UID.</param>
        /// <param name="referencedSopInstanceUid">SOP Instance UID.</param>
        /// <param name="retrieveUrl">Optional WADO-RS retrieve URL for this instance.</param>
        public DicomStowInstanceResult(
            string referencedSopClassUid,
            string referencedSopInstanceUid,
            string? retrieveUrl = null)
        {
            ReferencedSopClassUid = referencedSopClassUid;
            ReferencedSopInstanceUid = referencedSopInstanceUid;
            RetrieveUrl = retrieveUrl;
            FailureReason = null;
        }

        /// <summary>
        /// Creates a failed instance result (Failed SOP Sequence item).
        /// </summary>
        /// <param name="referencedSopClassUid">SOP Class UID.</param>
        /// <param name="referencedSopInstanceUid">SOP Instance UID.</param>
        /// <param name="failureReason">DICOM failure reason code (PS3.4 Annex CC).</param>
        public DicomStowInstanceResult(
            string referencedSopClassUid,
            string referencedSopInstanceUid,
            ushort failureReason)
        {
            ReferencedSopClassUid = referencedSopClassUid;
            ReferencedSopInstanceUid = referencedSopInstanceUid;
            RetrieveUrl = null;
            FailureReason = failureReason;
        }
    }

    /// <summary>
    /// A STOW-RS response indicating that all submitted instances were successfully stored
    /// (HTTP 200 OK).
    /// <para>
    /// The framework serializes the <see cref="StoredInstances"/> list as the Referenced SOP
    /// Sequence (0008,1199) in the STOW-RS Response Module (PS3.18 Section 10.5.1).
    /// </para>
    /// </summary>
    public class DicomStowSuccessResponse : IDicomStowResponse
    {
        /// <summary>
        /// Per-instance results for all successfully stored instances.
        /// Each item contains the SOP Class UID, SOP Instance UID, and optional retrieve URL.
        /// </summary>
        public IList<DicomStowInstanceResult> StoredInstances { get; }

        /// <summary>
        /// Creates a full-success response with the given per-instance results.
        /// </summary>
        public DicomStowSuccessResponse(IList<DicomStowInstanceResult> storedInstances)
        {
            StoredInstances = storedInstances;
        }
    }

    /// <summary>
    /// A STOW-RS response indicating that some instances were stored and some failed
    /// (HTTP 202 Accepted).
    /// <para>
    /// The framework serializes <see cref="StoredInstances"/> as the Referenced SOP Sequence
    /// (0008,1199) and <see cref="FailedInstances"/> as the Failed SOP Sequence (0008,1198)
    /// in the STOW-RS Response Module (PS3.18 Section 10.5.1).
    /// </para>
    /// </summary>
    public class DicomStowPartialSuccessResponse : IDicomStowResponse
    {
        /// <summary>Per-instance results for instances that were successfully stored.</summary>
        public IList<DicomStowInstanceResult> StoredInstances { get; }

        /// <summary>
        /// Per-instance results for instances that could not be stored.
        /// Each item includes a <see cref="DicomStowInstanceResult.FailureReason"/> code.
        /// </summary>
        public IList<DicomStowInstanceResult> FailedInstances { get; }

        /// <summary>
        /// Creates a partial-success response.
        /// </summary>
        /// <param name="storedInstances">Successfully stored instances (may be empty).</param>
        /// <param name="failedInstances">Failed instances (must contain at least one item).</param>
        public DicomStowPartialSuccessResponse(
            IList<DicomStowInstanceResult> storedInstances,
            IList<DicomStowInstanceResult> failedInstances)
        {
            StoredInstances = storedInstances;
            FailedInstances = failedInstances;
        }
    }
}
