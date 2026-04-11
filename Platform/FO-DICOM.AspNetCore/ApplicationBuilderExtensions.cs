// Copyright (c) 2012-2023 fo-dicom contributors.
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
                endpoints.MapGet($"{urlPrefix}/studies", async context =>
                {
                    // Resolve the dependency from the service provider
                    var dicomWebService = context.RequestServices.GetService<IDicomWebService>();
                    if (dicomWebService is null)
                    {
                        throw new InvalidOperationException("IDicomWebService service not registered. Please create an implementation of the abstract DicomWebService and inject this into the service collection.");
                    }

                    await dicomWebService.HandleQidoStudiesRequestAsync(context);
                });

                // TODO PJ: Add more endpoints here...
            });

            return app;
        }

    }
}
