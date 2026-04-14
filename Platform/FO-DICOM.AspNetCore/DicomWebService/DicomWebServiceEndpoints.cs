// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore
{
    /// <summary>
    /// Endpoint metadata attached to every DICOMweb route by
    /// <see cref="ApplicationBuilderExtensions.MapDicomWebService"/>.
    /// The <see cref="DicomWebService.WadoResponseWriter"/> reads this at request time to
    /// construct <c>Content-Location</c> headers without string-parsing the request URL.
    /// </summary>
    internal sealed class DicomWebEndpointMetadata
    {
        /// <summary>
        /// The URL prefix under which all DICOMweb endpoints are mounted,
        /// e.g. <c>"/dicomweb"</c>. Never has a trailing slash.
        /// </summary>
        internal string UrlPrefix { get; }

        internal DicomWebEndpointMetadata(string urlPrefix) => UrlPrefix = urlPrefix;
    }

    public static partial class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Maps all DICOMweb endpoints (QIDO-RS and WADO-RS) under the given URL prefix.
        /// <para>
        /// Returns a <see cref="RouteGroupBuilder"/> so callers can chain endpoint metadata such as
        /// <c>.RequireAuthorization()</c>, <c>.RequireCors()</c>, or custom <c>.WithMetadata()</c>.
        /// </para>
        /// <example>
        /// <code>
        /// // Basic usage:
        /// app.MapDicomWebService("/dicomweb");
        ///
        /// // With endpoint metadata:
        /// app.MapDicomWebService("/dicomweb")
        ///    .RequireAuthorization()
        ///    .RequireCors("AllowDicomViewers");
        /// </code>
        /// </example>
        /// </summary>
        /// <param name="endpoints">The endpoint route builder (typically a <see cref="WebApplication"/>).</param>
        /// <param name="urlPrefix">The URL prefix for all DICOMweb endpoints (e.g. <c>"/dicomweb"</c>).</param>
        /// <returns>A <see cref="RouteGroupBuilder"/> that can be used to add metadata to all mapped endpoints.</returns>
        public static RouteGroupBuilder MapDicomWebService(this IEndpointRouteBuilder endpoints, string urlPrefix)
        {
            urlPrefix = urlPrefix.TrimEnd('/');
            urlPrefix = urlPrefix.StartsWith("/") ? urlPrefix : $"/{urlPrefix}";

            var group = endpoints.MapGroup(urlPrefix);

            // ── QIDO-RS: Studies ───────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: All Studies
            group.MapGet("/studies", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoStudiesRequestAsync(ctx)));

            // ── QIDO-RS: Series ────────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: Study's Series (studyInstanceUID scopes the search)
            group.MapGet("/studies/{studyInstanceUID}/series", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoSeriesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: All Series (relational, no study scope)
            group.MapGet("/series", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoSeriesRequestAsync(ctx)));

            // ── QIDO-RS: Instances ─────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: Study's Series' Instances (both UIDs scope the search)
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: Study's Instances (studyInstanceUID scopes the search)
            group.MapGet("/studies/{studyInstanceUID}/instances", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: All Instances (relational, no study/series scope)
            group.MapGet("/instances", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            // ── WADO-RS: Instance Resources (PS3.18 Table 10.4.1-1) ────────────
            // Retrieve all instances in a study
            group.MapGet("/studies/{studyInstanceUID}", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoInstancesRequestAsync(ctx)));

            // Retrieve all instances in a series
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoInstancesRequestAsync(ctx)));

            // Retrieve a single instance
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoInstancesRequestAsync(ctx)));

            // ── WADO-RS: Metadata Resources (PS3.18 Table 10.4.1-2) ────────────
            // Study-level metadata
            group.MapGet("/studies/{studyInstanceUID}/metadata", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoMetadataRequestAsync(ctx)));

            // Series-level metadata
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/metadata", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoMetadataRequestAsync(ctx)));

            // Instance-level metadata
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/metadata", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoMetadataRequestAsync(ctx)));

            // ── WADO-RS: Frame Resources (PS3.18 Section 10.4.1.1.4) ───────────
            // Retrieve specific frames of a single instance
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/frames/{frameList}", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoFramesRequestAsync(ctx)));

            // ── WADO-RS: Bulk Data Resources (PS3.18 Section 10.4.1.1.5) ────────
            // Retrieve a bulk data element identified by a tag path (catch-all after /bulk/).
            // The {**bulkPath} segment captures paths like "7FE00010" (top-level) or
            // "54000100/0/54001010" (nested sequence item element).
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances/{sopInstanceUID}/bulk/{**bulkPath}", (HttpContext context) =>
                HandleDicomWebAsync(context, (svc, ctx) => svc.HandleWadoBulkDataRequestAsync(ctx)));

            // Attach the URL prefix as endpoint metadata so WadoResponseWriter can build
            // Content-Location headers without parsing the request URL at runtime.
            group.WithMetadata(new DicomWebEndpointMetadata(urlPrefix));

            return group;
        }

        private static Task HandleDicomWebAsync(HttpContext context,
            Func<DicomWebService.IDicomWebService, HttpContext, Task> handler)
        {
            var dicomWebService = context.RequestServices.GetService<DicomWebService.IDicomWebService>();
            if (dicomWebService is null)
            {
                throw new InvalidOperationException(
                    "IDicomWebService service not registered. " +
                    "Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
            }
            return handler(dicomWebService, context);
        }
    }
}
