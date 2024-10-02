using FellowOakDicom.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FellowOakDicom.DicomWeb
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer(); //TODO PJ: try to get swagger working by default for dicomweb?
            builder.Services.AddSwaggerGen();
            
            builder.Services.AddFellowOakDicom();
            builder.Services.AddDicomWebServer<MyDicomWebServer>();
            
            
            

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.MapDicomWebServer("/dicomweb");

            app.Run();
        }
    }
}