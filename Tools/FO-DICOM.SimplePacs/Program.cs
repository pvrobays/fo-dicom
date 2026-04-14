// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.AspNetCore;
using FellowOakDicom.SimplePacs.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FellowOakDicom.SimplePacs
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddFellowOakDicom();

            // EF Core + SQLite — use IDbContextFactory so the singleton SimplePacsServer
            // can create per-request DbContext instances safely.
            builder.Services.AddDbContextFactory<SimplePacsDbContext>(options =>
            {
                var storageRoot = builder.Configuration["SimplePacs:StorageRoot"] ?? "dicom-storage";
                var dbPath = System.IO.Path.Combine(storageRoot, "simplepacs.db");
                options.UseSqlite($"Data Source={dbPath}");
            });

            builder.Services.AddDicomWebService<SimplePacsServer>();

            var app = builder.Build();

            // Ensure the storage directory and SQLite schema exist on startup.
            var appStorageRoot = builder.Configuration["SimplePacs:StorageRoot"] ?? "dicom-storage";
            System.IO.Directory.CreateDirectory(appStorageRoot);

            using (var scope = app.Services.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SimplePacsDbContext>>();
                using var db = factory.CreateDbContext();
                db.Database.EnsureCreated();
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.MapDicomWebService("/dicomweb");

            app.Run();
        }
    }
}
