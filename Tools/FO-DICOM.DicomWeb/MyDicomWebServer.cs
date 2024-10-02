using FellowOakDicom.AspNetCore.DicomWebServer;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.DicomWeb
{
    public class MyDicomWebServer : DicomWebServer, IDicomQidoProvider
    {
        public async Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, CancellationToken cancellationToken)
        {
            //TODO PJ: check authentication?
            
            var response = new DicomQidoSuccessResponse();
            for (var i = 0; i < 5; i++)
            {
                var dicomDataset = request.Dataset.Clone();
                
                dicomDataset.AddOrUpdate(DicomTag.AccessionNumber, "123456-" + i);
                //TODO PJ: Need to add more tags here
                
                response.AddResult(dicomDataset);
            }
            return response;
        }
    }
}