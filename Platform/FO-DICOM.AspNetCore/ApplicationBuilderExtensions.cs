// Copyright (c) 2012-2023 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using FellowOakDicom.AspNetCore.DicomWebServer;
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

        public static IApplicationBuilder MapDicomWebServer(this IApplicationBuilder app, string urlPrefix)
        {
            urlPrefix = urlPrefix.TrimEnd('/');
            urlPrefix = urlPrefix.StartsWith("/") ? urlPrefix : $"/{urlPrefix}";
            
            app.UseFellowOakDicom(); //TODO PJ: necessary?
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet($"{urlPrefix}/studies", async context =>
                {
                    // Resolve the dependency from the service provider
                    var dicomWebServer = context.RequestServices.GetService<IDicomWebServer>();
                    if (dicomWebServer is null)
                    {
                        throw new InvalidOperationException("IDicomWebServer service not registered. Please create an implementation of the abstract DicomWebServer and inject this into the service collection.");
                    }

                    await dicomWebServer.HandleQidoStudiesRequestAsync(context);
                });

                // TODO PJ: Add more endpoints here...
            });

            return app;
        }

    }
}
