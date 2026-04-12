using FellowOakDicom.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FellowOakDicom.DicomWeb
{
    /**
     * Test Urls (see also test-dicomweb.ps1 for automated test commands):
     * 
     * Basic queries:
     *   https://localhost:7215/dicomweb/studies
     *   https://localhost:7215/dicomweb/studies?PatientID=11235813&StudyDate=20130509
     *   https://localhost:7215/dicomweb/studies?PatientID=11235813&includefield=00081048,00081049,00081060
     * 
     * Sequence support - includefield with dot notation:
     *   https://localhost:7215/dicomweb/studies?includefield=00081115.00080060
     *   https://localhost:7215/dicomweb/studies?includefield=OtherPatientIDsSequence.PatientID
     * 
     * Sequence support - includefield with bare SQ tag:
     *   https://localhost:7215/dicomweb/studies?includefield=RequestAttributesSequence
     * 
     * Sequence support - query filter with dot notation:
     *   https://localhost:7215/dicomweb/studies?00101002.00100020=11235813
     *   https://localhost:7215/dicomweb/studies?OtherPatientIDsSequence.PatientID=11235813
     *   https://localhost:7215/dicomweb/studies?00100010=SMITH*&00101002.00100020=11235813&limit=25
     * 
     * Combined:
     *   https://localhost:7215/dicomweb/studies?PatientID=11235813&includefield=OtherPatientIDsSequence.PatientID&includefield=RequestAttributesSequence
     */
    
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            
            builder.Services.AddFellowOakDicom();
            builder.Services.AddDicomWebService<MyDicomWebServer>();
            
            
            

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

            app.MapDicomWebService("/dicomweb")
                .RequireRateLimiting("fixed"); //example of what you can do with the returned RouteGroupBuilder - apply metadata to all DICOMweb endpoints at once (e.g. auth, CORS, rate limiting, etc.)

            app.Run();
        }
    }
}