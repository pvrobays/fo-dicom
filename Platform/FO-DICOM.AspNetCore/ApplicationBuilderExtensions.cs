// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.AspNetCore.DicomWebService;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FellowOakDicom.AspNetCore
{
    public static class ApplicationBuilderExtensions
    {

        public static IApplicationBuilder UseFellowOakDicom(this IApplicationBuilder app)
        {
            DicomSetupBuilder.UseServiceProvider(app.ApplicationServices);
            return app;
        }

        public static IApplicationBuilder MapDicomWebService(this IApplicationBuilder app, string urlPrefix)
        {
            urlPrefix = urlPrefix.TrimEnd('/');
            urlPrefix = urlPrefix.StartsWith("/") ? urlPrefix : $"/{urlPrefix}";
            
            app.UseFellowOakDicom(); //TODO PJ: necessary?
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // ── Studies ────────────────────────────────────────────────────────────
                // PS3.18 Table 10.6.1-1: All Studies
                endpoints.MapGet($"{urlPrefix}/studies", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoStudiesRequestAsync(context);
                });

                // ── Series ─────────────────────────────────────────────────────────────
                // PS3.18 Table 10.6.1-1: Study's Series (studyInstanceUID scopes the search)
                endpoints.MapGet($"{urlPrefix}/studies/{{studyInstanceUID}}/series", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoSeriesRequestAsync(context);
                });

                // PS3.18 Table 10.6.1-1: All Series (relational, no study scope)
                endpoints.MapGet($"{urlPrefix}/series", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoSeriesRequestAsync(context);
                });

                // ── Instances ──────────────────────────────────────────────────────────
                // PS3.18 Table 10.6.1-1: Study's Series' Instances (both UIDs scope the search)
                endpoints.MapGet($"{urlPrefix}/studies/{{studyInstanceUID}}/series/{{seriesInstanceUID}}/instances", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoInstancesRequestAsync(context);
                });

                // PS3.18 Table 10.6.1-1: Study's Instances (studyInstanceUID scopes the search)
                endpoints.MapGet($"{urlPrefix}/studies/{{studyInstanceUID}}/instances", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoInstancesRequestAsync(context);
                });

                // PS3.18 Table 10.6.1-1: All Instances (relational, no study/series scope)
                endpoints.MapGet($"{urlPrefix}/instances", async context =>
                {
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    await dicomWebService.HandleQidoInstancesRequestAsync(context);
                });
            });

            return app;
        }

    }
}
