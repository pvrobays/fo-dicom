// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.AspNetCore.DicomWebService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore
{
    public static class ApplicationBuilderExtensions
    {

        public static IApplicationBuilder UseFellowOakDicom(this IApplicationBuilder app)
        {
            DicomSetupBuilder.UseServiceProvider(app.ApplicationServices);
            return app;
        }

        /// <summary>
        /// Maps all QIDO-RS endpoints (studies, series, instances) under the given URL prefix.
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

            // ── Studies ────────────────────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: All Studies
            group.MapGet("/studies", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoStudiesRequestAsync(ctx)));

            // ── Series ─────────────────────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: Study's Series (studyInstanceUID scopes the search)
            group.MapGet("/studies/{studyInstanceUID}/series", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoSeriesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: All Series (relational, no study scope)
            group.MapGet("/series", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoSeriesRequestAsync(ctx)));

            // ── Instances ──────────────────────────────────────────────────────────
            // PS3.18 Table 10.6.1-1: Study's Series' Instances (both UIDs scope the search)
            group.MapGet("/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: Study's Instances (studyInstanceUID scopes the search)
            group.MapGet("/studies/{studyInstanceUID}/instances", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            // PS3.18 Table 10.6.1-1: All Instances (relational, no study/series scope)
            group.MapGet("/instances", (HttpContext context) =>
                HandleAsync(context, (svc, ctx) => svc.HandleQidoInstancesRequestAsync(ctx)));

            return group;
        }

        private static Task HandleAsync(HttpContext context, Func<IDicomWebService, HttpContext, Task> handler)
        {
            var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
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
