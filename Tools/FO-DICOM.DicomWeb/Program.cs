using FellowOakDicom.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FellowOakDicom.DicomWeb
{
    /**
     * Test Urls:
     * https://localhost:7215/dicomweb/studies
     * https://localhost:7215/dicomweb/studies?PatientID=11235813&StudyDate=20130509
     * https://localhost:7215/dicomweb/studies?PatientID=11235813&includefield=00081048,00081049,00081060
     */
    
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