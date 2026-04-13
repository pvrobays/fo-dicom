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
        /// Constructs a WADO-RS request for the given UID scope.
        /// </summary>
        /// <param name="studyInstanceUid">Study Instance UID (required).</param>
        /// <param name="seriesInstanceUid">Series Instance UID, or <c>null</c> for study-level scope.</param>
        /// <param name="sopInstanceUid">SOP Instance UID, or <c>null</c> for study/series-level scope.</param>
        public DicomWadoRequest(string studyInstanceUid, string? seriesInstanceUid = null, string? sopInstanceUid = null)
        {
            StudyInstanceUid = studyInstanceUid;
            SeriesInstanceUid = seriesInstanceUid;
            SopInstanceUid = sopInstanceUid;
        }
    }
}
