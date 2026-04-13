// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Base class for DICOMweb failure responses shared across QIDO-RS, WADO-RS, and STOW-RS.
    /// Implements both <see cref="IDicomQidoResponse"/> and <see cref="IDicomWadoResponse"/> so
    /// that a single failure type can be returned from any provider method without casting.
    /// </summary>
    public abstract class DicomWebFailureResponse : IDicomQidoResponse, IDicomWadoResponse { }

    /// <summary>
    /// The request could not be understood or contained invalid parameters (HTTP 400).
    /// </summary>
    public class DicomWebBadRequestResponse : DicomWebFailureResponse
    {
        /// <summary>An optional human-readable description of why the request was rejected.</summary>
        public string? Reason { get; }

        public DicomWebBadRequestResponse(string? reason = null)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// Authentication is required and has not been provided (HTTP 401).
    /// </summary>
    public class DicomWebUnauthorizedResponse : DicomWebFailureResponse { }

    /// <summary>
    /// The server understood the request but refuses to authorize it (HTTP 403).
    /// </summary>
    public class DicomWebForbiddenResponse : DicomWebFailureResponse { }

    /// <summary>
    /// The requested resource does not exist (HTTP 404).
    /// </summary>
    public class DicomWebNotFoundResponse : DicomWebFailureResponse
    {
        /// <summary>An optional human-readable description of what was not found.</summary>
        public string? Reason { get; }

        public DicomWebNotFoundResponse(string? reason = null)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// The requested operation is not implemented by this server (HTTP 501).
    /// </summary>
    public class DicomWebNotImplementedResponse : DicomWebFailureResponse { }

    /// <summary>
    /// The server is temporarily unable to handle the request (HTTP 503).
    /// </summary>
    public class DicomWebUnavailableResponse : DicomWebFailureResponse
    {
        /// <summary>An optional human-readable description of the unavailability reason.</summary>
        public string? Reason { get; }

        public DicomWebUnavailableResponse(string? reason = null)
        {
            Reason = reason;
        }
    }
}
