using Bogus;
using FellowOakDicom.AspNetCore.DicomWebService;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.DicomWeb
{
    public class MyDicomWebServer : DicomWebService, IDicomQidoProvider
    {
        public readonly Faker _faker = new Faker();
        
        public async Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, CancellationToken cancellationToken)
        {
            //TODO PJ: check authentication?
            
            var response = new DicomQidoSuccessResponse
            {
                IsFuzzyMatchingSupported = false
            };
            
            //TODO PJ: Create a list of all columns to retrieve from the database
            
            //TODO PJ: Get the data columns from the database
            
            //TODO PJ: Create a DicomDataset for each row in the database
            for (var i = 0; i < 5; i++)
            {
                var dicomDataset = request.Dataset.Clone();
                
                foreach (DicomItem item in request.Dataset)
                {
                    switch (item.ValueRepresentation.Code)
                    {
                        case DicomVRCode.DA:
                            dicomDataset.AddOrUpdate(item.Tag, _faker.Date.Past());
                            break;
                        case DicomVRCode.TM:
                            dicomDataset.AddOrUpdate(item.Tag, _faker.Date.Past());
                            break;
                        case DicomVRCode.SH:
                            dicomDataset.AddOrUpdate(item.Tag, _faker.Random.String2(10));
                            break;
                        case DicomVRCode.PN:
                            dicomDataset.AddOrUpdate(item.Tag, $"{_faker.Name.LastName()}^{_faker.Name.FirstName()}");
                            break;
                        case DicomVRCode.UI:
                            dicomDataset.AddOrUpdate(item.Tag, DicomUID.Generate());
                            break;
                        case DicomVRCode.IS:
                            dicomDataset.AddOrUpdate(item.Tag, _faker.Random.Number(0, 1000));
                            break;
                        default:
                            break;
                    }
                }
                
                
                response.AddResult(dicomDataset);
            }
            
            return response;
        }
    }
}