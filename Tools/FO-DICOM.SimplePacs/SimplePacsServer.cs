// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.AspNetCore.DicomWebService;
using FellowOakDicom.DicomWeb;
using FellowOakDicom.SimplePacs.Data;
using FellowOakDicom.SimplePacs.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.SimplePacs
{
    /// <summary>
    /// Simple PACS DICOMweb server.
    /// Stores received DICOM files in a folder hierarchy under a configurable root directory
    /// and indexes metadata in a SQLite database for QIDO-RS queries.
    /// Implements STOW-RS, QIDO-RS, and WADO-RS (PS3.18).
    /// </summary>
    public class SimplePacsServer : DicomWebService,
        IDicomStowProvider,
        IDicomQidoProvider,
        IDicomWadoProvider
    {
        private readonly IDbContextFactory<SimplePacsDbContext> _dbFactory;
        private readonly DicomFileStore _fileStore;

        public SimplePacsServer(
            IDbContextFactory<SimplePacsDbContext> dbFactory,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
            _dbFactory = dbFactory;
            var storageRoot = configuration["SimplePacs:StorageRoot"] ?? "dicom-storage";
            _fileStore = new DicomFileStore(storageRoot);
        }

        // ── STOW-RS ───────────────────────────────────────────────────────────

        public Task<IDicomStowResponse> OnStoreInstancesAsync(
            DicomStowRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new System.NotImplementedException("STOW-RS not yet implemented");

        // ── QIDO-RS ───────────────────────────────────────────────────────────

        public Task<IDicomQidoResponse> OnQidoRequestAsync(
            DicomQidoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new System.NotImplementedException("QIDO-RS not yet implemented");

        // ── WADO-RS ───────────────────────────────────────────────────────────

        public Task<IDicomWadoInstanceResponse> OnRetrieveInstancesAsync(
            DicomWadoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new System.NotImplementedException("WADO-RS instances not yet implemented");

        public Task<IDicomWadoMetadataResponse> OnRetrieveMetadataAsync(
            DicomWadoRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new System.NotImplementedException("WADO-RS metadata not yet implemented");
    }
}
