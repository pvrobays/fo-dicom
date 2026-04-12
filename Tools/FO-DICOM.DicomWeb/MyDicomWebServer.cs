using Bogus;
using FellowOakDicom.AspNetCore.DicomWebService;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.DicomWeb
{
    public class MyDicomWebServer : DicomWebService, IDicomQidoProvider
    {
        public readonly Faker _faker = new Faker();
        
        public async Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken)
        {
            // httpContext.User  — inspect JWT claims (e.g. httpContext.User.FindFirst("sub"))
            // httpContext.Connection.ClientCertificate  — inspect mTLS client certificate
            // httpContext.Request.Headers  — inspect custom headers (e.g. X-Tenant-Id)
            
            var response = new DicomQidoSuccessResponse
            {
                IsFuzzyMatchingSupported = false
            };

            // Extract the StudyDate range from the request dataset (if present).
            // When StudyDate is stored as a DicomDateRange (i.e. the client sent a range string
            // like "20130101-20131231"), we use it to filter fake results to dates in that range.
            // When absent or a single-date string, we generate unrestricted fake dates.
            DicomDateRange studyDateFilter = null;
            if (request.Dataset.Contains(DicomTag.StudyDate))
            {
                var wireValue = request.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty);
                if (!string.IsNullOrEmpty(wireValue) && wireValue.Contains('-'))
                {
                    studyDateFilter = request.Dataset.GetSingleValue<DicomDateRange>(DicomTag.StudyDate);
                }
            }

            //TODO PJ: Create a list of all columns to retrieve from the database
            
            //TODO PJ: Get the data columns from the database
            
            //TODO PJ: Create a DicomDataset for each row in the database
            for (var i = 0; i < 5; i++)
            {
                var dicomDataset = request.Dataset.Clone();
                PopulateWithFakeData(dicomDataset, studyDateFilter);
                response.AddResult(dicomDataset);
            }
            
            return response;
        }

        /// <summary>
        /// Populates a DicomDataset with fake data based on the VR of each item.
        /// Recurses into sequence items to populate nested datasets as well.
        /// When <paramref name="studyDateFilter"/> is non-null, DA values for
        /// <see cref="DicomTag.StudyDate"/> are constrained to fall within that range.
        /// </summary>
        private void PopulateWithFakeData(DicomDataset dataset, DicomDateRange studyDateFilter = null)
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
                        if (item.Tag == DicomTag.StudyDate && studyDateFilter != null)
                        {
                            // Honour the requested date range: generate a random date within [min, max].
                            // DateTime.MinValue / MaxValue sentinels mean "unbounded" — clamp them to
                            // a sensible window so Bogus doesn't generate out-of-range dates.
                            var rangeMin = studyDateFilter.Minimum == DateTime.MinValue
                                ? DateTime.Today.AddYears(-10)
                                : studyDateFilter.Minimum;
                            var rangeMax = studyDateFilter.Maximum == DateTime.MaxValue
                                ? DateTime.Today
                                : studyDateFilter.Maximum;
                            dataset.AddOrUpdate(item.Tag, _faker.Date.Between(rangeMin, rangeMax));
                        }
                        else
                        {
                            dataset.AddOrUpdate(item.Tag, _faker.Date.Past());
                        }
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
