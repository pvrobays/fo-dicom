// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Represents a STOW-RS Store Transaction request (PS3.18 Section 10.5).
    /// <para>
    /// The framework parses the <c>multipart/related; type="application/dicom"</c> request body
    /// and exposes the resulting <see cref="DicomFile"/> objects to the provider via
    /// <see cref="Instances"/>. Study Instance UID scope enforcement (when the request targets
    /// <c>POST .../studies/{studyInstanceUID}</c>) is performed by the framework before the
    /// provider is invoked — instances whose Study Instance UID does not match the route are
    /// excluded from <see cref="Instances"/> and reported as failures in the response.
    /// </para>
    /// </summary>
    public class DicomStowRequest
    {
        /// <summary>
        /// The Study Instance UID from the request URL, or <c>null</c> for an unscoped
        /// <c>POST .../studies</c> request that accepts instances from any study.
        /// </summary>
        public string? StudyInstanceUid { get; }

        /// <summary>
        /// The DICOM instances parsed from the multipart request body that passed Study Instance
        /// UID validation (when <see cref="StudyInstanceUid"/> is non-null).
        /// Instances that failed validation are not included here; they appear as failures in
        /// the STOW-RS response module returned to the client.
        /// </summary>
        public IReadOnlyList<DicomFile> Instances { get; }

        /// <summary>
        /// Constructs a STOW-RS request with the parsed DICOM instances.
        /// </summary>
        /// <param name="studyInstanceUid">
        /// The Study Instance UID scope from the route, or <c>null</c> for an unscoped request.
        /// </param>
        /// <param name="instances">
        /// The validated DICOM instances to store. Must not be <c>null</c>.
        /// </param>
        public DicomStowRequest(string? studyInstanceUid, IReadOnlyList<DicomFile> instances)
        {
            StudyInstanceUid = studyInstanceUid;
            Instances = instances;
        }
    }
}
