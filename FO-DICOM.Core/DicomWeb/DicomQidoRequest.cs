using FellowOakDicom.Network;

namespace FellowOakDicom.DicomWeb
{
    public class DicomQidoRequest //TODO PJ: have shared base class with DicomQidoResponse?
    {
        #region CONSTRUCTORS
        public DicomQidoRequest(DicomQueryRetrieveLevel level, DicomQidoRequestOptions? options = null)
        {
            // when creating requests, one may be forced to use invalid UIDs. So turn off validation
            Dataset = new DicomDataset().NotValidated();
            Level = level;
            Options = options ?? DicomQidoRequestOptions.Default;
        }
        
        #endregion
        
        #region PROPERTIES
        public DicomDataset Dataset { get; set; }
        
        public DicomQidoRequestOptions Options { get; }

        public DicomQueryRetrieveLevel Level
        {
            get => Dataset.GetSingleValueOrDefault(DicomTag.QueryRetrieveLevel, DicomQueryRetrieveLevel.NotApplicable);
            private set
            {
                switch (value)
                {
                    case DicomQueryRetrieveLevel.Patient:
                    case DicomQueryRetrieveLevel.Study:
                    case DicomQueryRetrieveLevel.Series:
                    case DicomQueryRetrieveLevel.Image:
                        Dataset.AddOrUpdate(DicomTag.QueryRetrieveLevel, value.ToString().ToUpperInvariant());
                        break;
                    default:
                        Dataset.Remove(DicomTag.QueryRetrieveLevel);
                        break;
                }
            }
        }
        
        #endregion
        
        #region DELEGATES AND EVENTS

        /// <summary>
        /// Delegate for response received event handling.
        /// </summary>
        /// <param name="request">C-FIND request.</param>
        /// <param name="response">C-FIND response.</param>
        public delegate void ResponseDelegate(DicomQidoRequest request, IDicomQidoResponse response);

        /// <summary>
        /// Gets or sets the response received event handler.
        /// </summary>
        public ResponseDelegate? OnResponseReceived;

        #endregion
        
        #region METHODS
        
        /// <summary>
        /// Convenience method for creating a C-FIND study query.
        /// </summary>
        /// <param name="patientId">Patient ID.</param>
        /// <param name="patientName">Patient name.</param>
        /// <param name="studyDateTime">Time range of studies.</param>
        /// <param name="accession">Accession number.</param>
        /// <param name="studyId">Study ID.</param>
        /// <param name="modalitiesInStudy">Modalities in study.</param>
        /// <param name="studyInstanceUid">Study instance UID.</param>
        /// <returns>C-FIND study query object.</returns>
        public static DicomQidoRequest CreateStudyQuery(
            string? patientId = null,
            string? patientName = null,
            DicomDateRange? studyDateTime = null,
            string? accession = null,
            string? studyId = null,
            string? modalitiesInStudy = null,
            string? studyInstanceUid = null)
        {
            var req = new DicomQidoRequest(DicomQueryRetrieveLevel.Study);
            req.Dataset.Add(DicomTag.PatientID, patientId);
            req.Dataset.Add(DicomTag.PatientName, patientName);
            // req.Dataset.Add(DicomTag.IssuerOfPatientID, string.Empty); //Not according to QIDO standard
            // req.Dataset.Add(DicomTag.PatientSex, string.Empty); //Not according to QIDO standard
            // req.Dataset.Add(DicomTag.PatientBirthDate, string.Empty); //Not according to QIDO standard
            req.Dataset.Add(DicomTag.StudyInstanceUID, studyInstanceUid);
            req.Dataset.Add(DicomTag.ModalitiesInStudy, modalitiesInStudy);
            req.Dataset.Add(DicomTag.StudyID, studyId);
            req.Dataset.Add(DicomTag.AccessionNumber, accession);
            req.Dataset.Add(DicomTag.StudyDate, studyDateTime);
            req.Dataset.Add(DicomTag.StudyTime, studyDateTime);
            // req.Dataset.Add(DicomTag.StudyDescription, string.Empty); //Not according to QIDO standard
            req.Dataset.Add(DicomTag.NumberOfStudyRelatedSeries, string.Empty);
            req.Dataset.Add(DicomTag.NumberOfStudyRelatedInstances, string.Empty);
            return req;
        }
        
        //TODO PJ: add Patient, Series, Image (, Worklist) query methods
        
        #endregion
    }

    public class DicomQidoRequestOptions
    {
        /// <summary>
        /// If true, and it is supported then additional fuzzy semantic matching of person names shall be performed in the manner specified in the DICOM Conformance Statement for the service provider.
        /// If it is not supported, the response shall include the following HTTP/1.1 Warning header (see RFC 2616 Section 14.46):
        ///     Warning: 299 {SERVICE}: "The fuzzymatching parameter is not supported. Only literal matching has been performed."
        /// </summary>
        public bool IsFuzzyMatching { get; set; }

        /// <summary>
        /// The maximum number of results the client would like to receive in the response. Can be overruled by the server.
        /// If the number of results exceeds the maximum supported by the server, the server shall return the maximum supported results and the response shall include the following HTTP/1.1 Warning header (see RFC 2616 Section 14.46):
        /// Warning: 299 {SERVICE}: "The number of results exceeded the maximum supported by the server. Additional results can be requested."
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// The number of results to skip before starting to return results.
        /// If the offset query key is not specified or its value is less than zero then {skippedResults} is zero.
        /// The first result returned shall be result number ({skippedResults} + 1). The last result returned shall be result number ({skippedResults} + {maximumResults}). If ({skippedResults} + 1) exceeds {maximumResults} then no results are returned.
        /// </summary>
        public int Offset { get; set; }
        
        public DicomQidoRequestOptions(bool isFuzzyMatching, int limit, int offset)
        {
            IsFuzzyMatching = isFuzzyMatching;
            Limit = limit;
            Offset = offset;
        }

        public static DicomQidoRequestOptions Default => new DicomQidoRequestOptions(false, 0, 0);
    }
}