// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System;
using System.Collections.Generic;

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Marker interface for responses to QIDO-RS requests.
    /// Returned by <see cref="FellowOakDicom.AspNetCore.DicomWebService.IDicomQidoProvider.OnQidoRequestAsync"/>.
    /// </summary>
    public interface IDicomQidoResponse { }

    /// <summary>
    /// A successful QIDO-RS response containing matching datasets.
    /// </summary>
    public class DicomQidoSuccessResponse : IDicomQidoResponse
    {
        /// <summary>The matching datasets to be serialized and returned to the client.</summary>
        public IList<DicomDataset> Results { get; set; } = new List<DicomDataset>();

        /// <summary>
        /// Whether the server performed the fuzzy matching that was requested.
        /// When <c>false</c> and the request had <c>fuzzymatching=true</c>, a
        /// <c>Warning: 299</c> header is added to the response per PS3.18 Section 8.3.4.
        /// </summary>
        public bool IsFuzzyMatchingSupported { get; set; }

        /// <summary>
        /// Whether the server reached its maximum result limit and additional matching
        /// results exist. When <c>true</c>, a <c>Warning: 299</c> header is added to the
        /// response per PS3.18 Section 8.3.4.4.
        /// </summary>
        public bool IsServerMaximumResultsReached { get; set; }

        public void AddResult(DicomDataset dataset) => Results.Add(dataset);
    }

    /// <summary>
    /// Returned when the QIDO-RS request is too broad to be processed (HTTP 413).
    /// This is QIDO-specific; WADO-RS and STOW-RS do not use this status for the same purpose.
    /// </summary>
    public class DicomQidoRequestTooBroadResponse : DicomWebFailureResponse, IDicomQidoResponse { }

    // ── Obsolete aliases ─────────────────────────────────────────────────────────
    // These names existed before the failure responses were generalized to DicomWeb*.
    // Use the DicomWeb* equivalents instead.

    /// <inheritdoc cref="DicomWebFailureResponse"/>
    [Obsolete("Use DicomWebFailureResponse instead.")]
    public class DicomQidoFailureResponse : DicomWebFailureResponse { }

    /// <inheritdoc cref="DicomWebBadRequestResponse"/>
    [Obsolete("Use DicomWebBadRequestResponse instead.")]
    public class DicomQidoBadRequestResponse : DicomWebBadRequestResponse
    {
        public DicomQidoBadRequestResponse(string reason = null) : base(reason) { }
    }

    /// <inheritdoc cref="DicomWebUnauthorizedResponse"/>
    [Obsolete("Use DicomWebUnauthorizedResponse instead.")]
    public class DicomQidoUnauthorizedResponse : DicomWebUnauthorizedResponse { }

    /// <inheritdoc cref="DicomWebForbiddenResponse"/>
    [Obsolete("Use DicomWebForbiddenResponse instead.")]
    public class DicomQidoForbiddenResponse : DicomWebForbiddenResponse { }

    /// <inheritdoc cref="DicomWebUnavailableResponse"/>
    [Obsolete("Use DicomWebUnavailableResponse instead.")]
    public class DicomQidoUnavailableResponse : DicomWebUnavailableResponse
    {
        public DicomQidoUnavailableResponse(string reason = null) : base(reason) { }
    }

    /// <inheritdoc cref="DicomWebNotImplementedResponse"/>
    [Obsolete("Use DicomWebNotImplementedResponse instead.")]
    public class DicomQidoNotImplementedResponse : DicomWebNotImplementedResponse { }
}
