// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Microsoft.AspNetCore.Http;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Helper for reading DICOM UID route values injected by URL path templates
    /// (e.g. <c>/studies/{studyInstanceUID}/series/{seriesInstanceUID}</c>).
    /// </summary>
    internal static class RouteUidHelper
    {
        /// <summary>
        /// Attempts to read a named route value from <see cref="HttpRequest.RouteValues"/>.
        /// Returns the string value if present and non-empty, or <c>null</c> otherwise.
        /// </summary>
        internal static string? GetRouteUid(HttpContext context, string key)
        {
            if (context.Request.RouteValues.TryGetValue(key, out var value)
                && value is string uid
                && !string.IsNullOrEmpty(uid))
            {
                return uid;
            }
            return null;
        }
    }
}
