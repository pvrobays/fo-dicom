// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Maps <see cref="DicomWebFailureResponse"/> subtypes to the corresponding HTTP status codes
    /// and writes optional reason bodies. Shared by both QIDO-RS and WADO-RS response writers so
    /// the failure-handling logic lives in a single place.
    /// </summary>
    internal static class DicomWebFailureWriter
    {
        /// <summary>
        /// Writes the HTTP status code (and optional reason body) for a
        /// <see cref="DicomWebFailureResponse"/> to the response.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="response"/> is not a recognised failure subtype.
        /// </exception>
        internal static async Task WriteAsync(
            HttpContext context,
            DicomWebFailureResponse response,
            CancellationToken cancellationToken)
        {
            switch (response)
            {
                case DicomWebBadRequestResponse badRequestResponse:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    if (badRequestResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(badRequestResponse.Reason, cancellationToken);
                    }
                    break;

                case DicomWebUnauthorizedResponse _:
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    break;

                case DicomWebForbiddenResponse _:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    break;

                case DicomWebNotFoundResponse notFoundResponse:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    if (notFoundResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(notFoundResponse.Reason, cancellationToken);
                    }
                    break;

                case DicomWebNotImplementedResponse _:
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    break;

                case DicomWebUnavailableResponse unavailableResponse:
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    if (unavailableResponse.Reason != null)
                    {
                        await context.Response.WriteAsync(unavailableResponse.Reason, cancellationToken);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(response),
                        $"Unrecognised DICOMweb failure response type: {response.GetType().Name}");
            }
        }
    }
}
