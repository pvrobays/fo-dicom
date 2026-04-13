// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

#if !NET462

using FellowOakDicom.AspNetCore;
using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FellowOakDicom.Tests.DicomWeb
{
    /// <summary>
    /// Integration tests for <see cref="ApplicationBuilderExtensions.MapDicomWebService"/>.
    /// Verifies that the <see cref="IEndpointRouteBuilder"/>-based routing correctly resolves
    /// URL templates, injects route values, and enforces HTTP method constraints.
    /// Uses <see cref="TestServer"/> (Microsoft.AspNetCore.TestHost) to run a real middleware
    /// pipeline without a network socket.
    /// </summary>
    [Collection(TestCollections.General)]
    public class DicomWebRoutingTests
    {
        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="TestServer"/> with the given <see cref="IDicomWebService"/>
        /// registered under <paramref name="urlPrefix"/>.
        /// </summary>
        private static HttpClient BuildTestClient<T>(string urlPrefix = "/dicomweb")
            where T : DicomWebService, IDicomWebService
        {
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(
                            provider => (IDicomWebService)ActivatorUtilities.CreateInstance<T>(provider));
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService(urlPrefix));
                    });
                })
                .Build();

            host.Start();
            return host.GetTestServer().CreateClient();
        }

        /// <summary>
        /// A concrete <see cref="DicomWebService"/> + <see cref="IDicomQidoProvider"/> that
        /// captures the incoming <see cref="DicomQidoRequest"/> and returns an empty success.
        /// Thread-safe: the last captured request is stored in <see cref="LastRequest"/>.
        /// </summary>
        private class CapturingDicomWebService : DicomWebService, IDicomQidoProvider
        {
            /// <summary>The most recently received QIDO request, or <c>null</c> if none yet.</summary>
            public DicomQidoRequest LastRequest { get; private set; }

            public Task<IDicomQidoResponse> OnQidoRequestAsync(
                DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());
            }
        }

        /// <summary>
        /// A <see cref="DicomWebService"/> that does NOT implement <see cref="IDicomQidoProvider"/>,
        /// so every QIDO request returns 501 Not Implemented.
        /// </summary>
        private class NoProviderDicomWebService : DicomWebService
        {
        }

        /// <summary>
        /// A <see cref="DicomWebService"/> implementing both QIDO and WADO providers.
        /// Returns empty success responses so routing tests only check status codes.
        /// </summary>
        private class FullDicomWebService : DicomWebService, IDicomQidoProvider, IDicomWadoProvider
        {
            public Task<IDicomQidoResponse> OnQidoRequestAsync(
                DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
        }

        /// <summary>
        /// A <see cref="DicomWebService"/> that implements only WADO, not QIDO.
        /// QIDO requests should return 501; WADO requests should succeed.
        /// </summary>
        private class WadoOnlyDicomWebService : DicomWebService, IDicomWadoProvider
        {
            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
        }

        /// <summary>
        /// Captures the last WADO request so routing tests can assert on injected UIDs.
        /// </summary>
        private class CapturingWadoService : DicomWebService, IDicomQidoProvider, IDicomWadoProvider
        {
            public DicomWadoRequest LastWadoRequest { get; private set; }

            public Task<IDicomQidoResponse> OnQidoRequestAsync(
                DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
                => Task.FromResult<IDicomQidoResponse>(new DicomQidoSuccessResponse());

            public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
            {
                LastWadoRequest = request;
                return Task.FromResult<IDicomWadoInstanceResponse>(new DicomWadoInstancesResponse(new List<DicomFile>()));
            }

            public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
                DicomWadoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
            {
                LastWadoRequest = request;
                return Task.FromResult<IDicomWadoMetadataResponse>(new DicomWadoMetadataResponse(new List<DicomDataset>()));
            }
        }

        // ─── Tests ────────────────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task MapDicomWebService_GetAllStudies_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetAllSeries_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/series");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetAllInstances_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/instances");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetStudySeries_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3.4.5/series");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetStudyInstances_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3.4.5/instances");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetStudySeriesInstances_Returns200()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/instances");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_StudySeriesRoute_InjectsStudyInstanceUidIntoRequest()
        {
            // Build the test server and retain a reference to the service so we can
            // inspect the captured request after the HTTP call completes.
            var service = new CapturingDicomWebService();

            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();

            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.840.99999/series");

            Assert.NotNull(service.LastRequest);
            Assert.Equal("1.2.840.99999",
                service.LastRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task MapDicomWebService_StudySeriesInstancesRoute_InjectsBothUidsIntoRequest()
        {
            var service = new CapturingDicomWebService();

            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();

            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/instances");

            Assert.NotNull(service.LastRequest);
            Assert.Equal("1.2.3",
                service.LastRequest.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty));
            Assert.Equal("4.5.6",
                service.LastRequest.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty));
        }

        [FactForNetCore]
        public async Task MapDicomWebService_NoQidoProvider_Returns501()
        {
            using var client = BuildTestClient<NoProviderDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies");

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_CustomPrefix_RoutesResolveUnderThatPrefix()
        {
            using var client = BuildTestClient<CapturingDicomWebService>(urlPrefix: "/wado");

            // Custom prefix works
            var response = await client.GetAsync("/wado/studies");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Default prefix is NOT mapped
            var notFound = await client.GetAsync("/dicomweb/studies");
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_PostToStudies_Returns405MethodNotAllowed()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.PostAsync("/dicomweb/studies", new StringContent(string.Empty));

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_UnknownPath_Returns404()
        {
            using var client = BuildTestClient<CapturingDicomWebService>();

            var response = await client.GetAsync("/dicomweb/patients");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_PrefixWithoutLeadingSlash_NormalisedAndRoutesResolve()
        {
            using var client = BuildTestClient<CapturingDicomWebService>(urlPrefix: "dicomweb");

            var response = await client.GetAsync("/dicomweb/studies");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_PrefixWithTrailingSlash_NormalisedAndRoutesResolve()
        {
            using var client = BuildTestClient<CapturingDicomWebService>(urlPrefix: "/dicomweb/");

            var response = await client.GetAsync("/dicomweb/studies");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ─── WADO-RS routing ──────────────────────────────────────────────────────

        [FactForNetCore]
        public async Task MapDicomWebService_GetStudyInstances_Wado_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3.4.5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetSeriesInstances_Wado_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetSingleInstance_Wado_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetStudyMetadata_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/metadata");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetSeriesMetadata_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/metadata");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_GetInstanceMetadata_Returns200()
        {
            using var client = BuildTestClient<FullDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9/metadata");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoNoProvider_Returns501()
        {
            using var client = BuildTestClient<NoProviderDicomWebService>();

            var response = await client.GetAsync("/dicomweb/studies/1.2.3.4.5");

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoOnly_QidoReturns501_WadoReturns200()
        {
            using var client = BuildTestClient<WadoOnlyDicomWebService>();

            var qidoResponse = await client.GetAsync("/dicomweb/studies");
            Assert.Equal(HttpStatusCode.NotImplemented, qidoResponse.StatusCode);

            var wadoResponse = await client.GetAsync("/dicomweb/studies/1.2.3");
            Assert.Equal(HttpStatusCode.OK, wadoResponse.StatusCode);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoStudyRoute_InjectsStudyUidIntoRequest()
        {
            var service = new CapturingWadoService();
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();
            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.840.99999");

            Assert.NotNull(service.LastWadoRequest);
            Assert.Equal("1.2.840.99999", service.LastWadoRequest.StudyInstanceUid);
            Assert.Null(service.LastWadoRequest.SeriesInstanceUid);
            Assert.Null(service.LastWadoRequest.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoSeriesRoute_InjectsStudyAndSeriesUid()
        {
            var service = new CapturingWadoService();
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();
            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6");

            Assert.NotNull(service.LastWadoRequest);
            Assert.Equal("1.2.3", service.LastWadoRequest.StudyInstanceUid);
            Assert.Equal("4.5.6", service.LastWadoRequest.SeriesInstanceUid);
            Assert.Null(service.LastWadoRequest.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoInstanceRoute_InjectsAllThreeUids()
        {
            var service = new CapturingWadoService();
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();
            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.3/series/4.5.6/instances/7.8.9");

            Assert.NotNull(service.LastWadoRequest);
            Assert.Equal("1.2.3", service.LastWadoRequest.StudyInstanceUid);
            Assert.Equal("4.5.6", service.LastWadoRequest.SeriesInstanceUid);
            Assert.Equal("7.8.9", service.LastWadoRequest.SopInstanceUid);
        }

        [FactForNetCore]
        public async Task MapDicomWebService_WadoMetadataRoute_InjectsStudyUidIntoRequest()
        {
            var service = new CapturingWadoService();
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddFellowOakDicom();
                        services.AddRouting();
                        services.AddSingleton<IDicomWebService>(service);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapDicomWebService("/dicomweb"));
                    });
                })
                .Build();
            host.Start();
            var client = host.GetTestServer().CreateClient();

            await client.GetAsync("/dicomweb/studies/1.2.3/metadata");

            Assert.NotNull(service.LastWadoRequest);
            Assert.Equal("1.2.3", service.LastWadoRequest.StudyInstanceUid);
            Assert.Null(service.LastWadoRequest.SeriesInstanceUid);
        }
    }
}

#endif
