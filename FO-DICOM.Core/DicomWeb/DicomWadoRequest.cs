// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Represents a WADO-RS retrieve request for either DICOM instances or metadata
    /// at study, series, or instance level (PS3.18 Section 10.4).
    /// </summary>
    public class DicomWadoRequest
    {
        /// <summary>
        /// The Study Instance UID from the request URL (always present).
        /// </summary>
        public string StudyInstanceUid { get; }

        /// <summary>
        /// The Series Instance UID from the request URL, or <c>null</c> for study-level requests.
        /// </summary>
        public string? SeriesInstanceUid { get; }

        /// <summary>
        /// The SOP Instance UID from the request URL, or <c>null</c> for study- or series-level requests.
        /// </summary>
        public string? SopInstanceUid { get; }

        /// <summary>
        /// The specific transfer syntax requested by the client via the <c>transfer-syntax</c> parameter
        /// of the <c>Accept</c> header (PS3.18 Section 8.7), or <c>null</c> when the client either
        /// sent <c>transfer-syntax=*</c> (see <see cref="AcceptsAnyTransferSyntax"/>) or omitted the
        /// parameter (in which case the framework defaults to Explicit VR Little Endian).
        /// </summary>
        public DicomTransferSyntax? RequestedTransferSyntax { get; }

        /// <summary>
        /// <c>true</c> when the client sent <c>transfer-syntax=*</c> in the <c>Accept</c> header,
        /// indicating it will accept instances in any transfer syntax.
        /// <para>
        /// When this is <c>true</c>, the framework will not attempt to transcode <see cref="DicomFile"/>
        /// responses — instances will be returned in whatever transfer syntax they were stored in.
        /// Providers may use this flag as a hint to skip any local transcoding as well.
        /// </para>
        /// </summary>
        public bool AcceptsAnyTransferSyntax { get; }

        /// <summary>
        /// Constructs a WADO-RS request for the given UID scope with optional transfer-syntax preference.
        /// </summary>
        /// <param name="studyInstanceUid">Study Instance UID (required).</param>
        /// <param name="seriesInstanceUid">Series Instance UID, or <c>null</c> for study-level scope.</param>
        /// <param name="sopInstanceUid">SOP Instance UID, or <c>null</c> for study/series-level scope.</param>
        /// <param name="requestedTransferSyntax">
        /// Specific transfer syntax from the <c>Accept</c> header, or <c>null</c> for wildcard/default.
        /// </param>
        /// <param name="acceptsAnyTransferSyntax">
        /// <c>true</c> when the client sent <c>transfer-syntax=*</c>.
        /// </param>
        public DicomWadoRequest(
            string studyInstanceUid,
            string? seriesInstanceUid = null,
            string? sopInstanceUid = null,
            DicomTransferSyntax? requestedTransferSyntax = null,
            bool acceptsAnyTransferSyntax = false)
        {
            StudyInstanceUid = studyInstanceUid;
            SeriesInstanceUid = seriesInstanceUid;
            SopInstanceUid = sopInstanceUid;
            RequestedTransferSyntax = requestedTransferSyntax;
            AcceptsAnyTransferSyntax = acceptsAnyTransferSyntax;
        }
    }
}
