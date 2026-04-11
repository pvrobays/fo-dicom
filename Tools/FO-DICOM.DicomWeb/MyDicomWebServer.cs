using Bogus;
using FellowOakDicom.AspNetCore.DicomWebService;
using System.Linq;
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
                PopulateWithFakeData(dicomDataset);
                response.AddResult(dicomDataset);
            }
            
            return response;
        }

        /// <summary>
        /// Populates a DicomDataset with fake data based on the VR of each item.
        /// Recurses into sequence items to populate nested datasets as well.
        /// </summary>
        private void PopulateWithFakeData(DicomDataset dataset)
        {
            foreach (DicomItem item in dataset.ToList())
            {
                if (item is DicomSequence sequence)
                {
                    // Recurse into each sequence item and populate with fake data
                    foreach (var sequenceItem in sequence.Items)
                    {
                        PopulateWithFakeData(sequenceItem);
                    }

                    // If the sequence is empty (bare include field), add one fake item
                    if (sequence.Items.Count == 0)
                    {
                        var fakeItem = new DicomDataset().NotValidated();
                        // Add a couple of common child attributes based on the sequence
                        fakeItem.AddOrUpdate(DicomTag.ReferencedSOPClassUID, DicomUID.Generate());
                        fakeItem.AddOrUpdate(DicomTag.ReferencedSOPInstanceUID, DicomUID.Generate());
                        sequence.Items.Add(fakeItem);
                    }

                    continue;
                }

                switch (item.ValueRepresentation.Code)
                {
                    case DicomVRCode.DA:
                        dataset.AddOrUpdate(item.Tag, _faker.Date.Past());
                        break;
                    case DicomVRCode.TM:
                        dataset.AddOrUpdate(item.Tag, _faker.Date.Past());
                        break;
                    case DicomVRCode.SH:
                        dataset.AddOrUpdate(item.Tag, _faker.Random.String2(10));
                        break;
                    case DicomVRCode.LO:
                        dataset.AddOrUpdate(item.Tag, _faker.Random.String2(16));
                        break;
                    case DicomVRCode.CS:
                        dataset.AddOrUpdate(item.Tag, _faker.PickRandom("CT", "MR", "US", "XR", "PT"));
                        break;
                    case DicomVRCode.PN:
                        dataset.AddOrUpdate(item.Tag, $"{_faker.Name.LastName()}^{_faker.Name.FirstName()}");
                        break;
                    case DicomVRCode.UI:
                        dataset.AddOrUpdate(item.Tag, DicomUID.Generate());
                        break;
                    case DicomVRCode.IS:
                        dataset.AddOrUpdate(item.Tag, _faker.Random.Number(0, 1000));
                        break;
                    default:
                        break;
                }
            }
        }
    }
}