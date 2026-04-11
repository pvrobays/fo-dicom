using FellowOakDicom.Network;
using System;

namespace FellowOakDicom.DicomWeb
{
    public class DicomQidoRequest
    {
        #region CONSTRUCTORS
        public DicomQidoRequest(DicomQueryRetrieveLevel level, DicomQidoRequestOptions? options = null)
        {
            // when creating requests, one may be forced to use invalid UIDs. So turn off validation
            Dataset = new DicomDataset().NotValidated();
            Level = level;
            Options = options ?? DicomQidoRequestOptions.Default;
            AddMinimumRequiredResponseTags();
        }

        private void AddMinimumRequiredResponseTags()
        {
            switch (Level)
            {
                case DicomQueryRetrieveLevel.Patient:
                    return; //not supported
                case DicomQueryRetrieveLevel.Study:
                    AddIfNotExists(DicomTag.StudyDate);
                    AddIfNotExists(DicomTag.StudyTime);
                    AddIfNotExists(DicomTag.AccessionNumber);
                    AddIfNotExists(DicomTag.InstanceAvailability);
                    AddIfNotExists(DicomTag.ModalitiesInStudy);
                    AddIfNotExists(DicomTag.ReferringPhysicianName);
                    AddIfNotExists(DicomTag.PatientName);
                    AddIfNotExists(DicomTag.PatientID);
                    AddIfNotExists(DicomTag.PatientBirthDate);
                    AddIfNotExists(DicomTag.PatientSex);
                    AddIfNotExists(DicomTag.StudyInstanceUID);
                    AddIfNotExists(DicomTag.StudyID);
                    AddIfNotExists(DicomTag.NumberOfStudyRelatedSeries);
                    AddIfNotExists(DicomTag.NumberOfStudyRelatedInstances);
                    break;
                case DicomQueryRetrieveLevel.Series:
                    // Required response attributes per PS3.18 Table 10.6.3-4
                    AddIfNotExists(DicomTag.Modality);
                    AddIfNotExists(DicomTag.SeriesDescription);
                    AddIfNotExists(DicomTag.SeriesInstanceUID);
                    AddIfNotExists(DicomTag.SeriesNumber);
                    AddIfNotExists(DicomTag.NumberOfSeriesRelatedInstances);
                    AddIfNotExists(DicomTag.PerformedProcedureStepStartDate);
                    AddIfNotExists(DicomTag.PerformedProcedureStepStartTime);
                    // RequestAttributesSequence (0040,0275) with required children
                    if (!Dataset.Contains(DicomTag.RequestAttributesSequence))
                    {
                        var item = new DicomDataset().NotValidated();
                        item.Add(DicomTag.ScheduledProcedureStepID, string.Empty);
                        item.Add(DicomTag.RequestedProcedureID, string.Empty);
                        Dataset.Add(new DicomSequence(DicomTag.RequestAttributesSequence, item));
                    }
                    break;
                case DicomQueryRetrieveLevel.Image:
                    // Required response attributes per PS3.18 Table 10.6.3-5
                    AddIfNotExists(DicomTag.SOPClassUID);
                    AddIfNotExists(DicomTag.SOPInstanceUID);
                    AddIfNotExists(DicomTag.InstanceAvailability);
                    AddIfNotExists(DicomTag.InstanceNumber);
                    AddIfNotExists(DicomTag.Rows);
                    AddIfNotExists(DicomTag.Columns);
                    AddIfNotExists(DicomTag.BitsAllocated);
                    AddIfNotExists(DicomTag.NumberOfFrames);
                    break;
                case DicomQueryRetrieveLevel.Worklist:
                    return; //not supported
                case DicomQueryRetrieveLevel.NotApplicable:
                    return; //not supported
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void AddIfNotExists(DicomTag dicomTag)
        {
            if (!Dataset.Contains(dicomTag))
            {
                Dataset.Add(dicomTag, string.Empty);
            }
        }

        #endregion
        
        #region PROPERTIES
        public DicomDataset Dataset { get; set; }
        
        public DicomQidoRequestOptions Options { get; }

        /// <summary>
        /// If true, the client requested that all available attributes be included in the response
        /// (i.e. <c>includefield=all</c> was present in the query string).
        /// The provider is responsible for interpreting this and returning all attributes it has available.
        /// Per PS3.18 Section 8.3.4.3, this parameter is mutually exclusive with any other includefield values.
        /// </summary>
        public bool IncludeAllFields { get; set; }

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
            req.Dataset.AddOrUpdate(DicomTag.PatientID, patientId);
            req.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);
            // req.Dataset.AddOrUpdate(DicomTag.IssuerOfPatientID, string.Empty); //Not according to QIDO standard
            // req.Dataset.AddOrUpdate(DicomTag.PatientSex, string.Empty); //Not according to QIDO standard
            // req.Dataset.AddOrUpdate(DicomTag.PatientBirthDate, string.Empty); //Not according to QIDO standard
            req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, modalitiesInStudy);
            req.Dataset.AddOrUpdate(DicomTag.StudyID, studyId);
            req.Dataset.AddOrUpdate(DicomTag.AccessionNumber, accession);
            req.Dataset.AddOrUpdate<DicomDateRange>(DicomTag.StudyDate, studyDateTime);
            req.Dataset.AddOrUpdate<DicomDateRange>(DicomTag.StudyTime, studyDateTime);
            // req.Dataset.AddOrUpdate(DicomTag.StudyDescription, string.Empty); //Not according to QIDO standard
            req.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedSeries, string.Empty);
            req.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedInstances, string.Empty);
            return req;
        }
        
        /// <summary>
        /// Convenience method for creating a QIDO-RS series query.
        /// </summary>
        /// <param name="studyInstanceUid">
        /// Study Instance UID to scope the query (sets the hierarchical parent).
        /// Pass <c>null</c> for a relational "all series" query across all studies.
        /// </param>
        /// <param name="modality">Modality to match (e.g. "CT", "MR"). <c>null</c> matches any modality.</param>
        /// <param name="seriesInstanceUid">Series Instance UID for exact-match filtering. <c>null</c> returns all series.</param>
        /// <param name="seriesNumber">Series number to match. <c>null</c> matches any.</param>
        /// <returns>QIDO-RS series query object.</returns>
        public static DicomQidoRequest CreateSeriesQuery(
            string? studyInstanceUid = null,
            string? modality = null,
            string? seriesInstanceUid = null,
            string? seriesNumber = null)
        {
            var req = new DicomQidoRequest(DicomQueryRetrieveLevel.Series);
            req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.Modality, modality);
            req.Dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, seriesInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.SeriesNumber, seriesNumber);
            req.Dataset.AddOrUpdate(DicomTag.NumberOfSeriesRelatedInstances, string.Empty);
            return req;
        }

        /// <summary>
        /// Convenience method for creating a QIDO-RS instance query.
        /// </summary>
        /// <param name="studyInstanceUid">
        /// Study Instance UID to scope the query. Pass <c>null</c> for a relational "all instances" query.
        /// </param>
        /// <param name="seriesInstanceUid">
        /// Series Instance UID to scope the query. Pass <c>null</c> to search across all series within the study.
        /// </param>
        /// <param name="sopInstanceUid">SOP Instance UID for exact-match filtering. <c>null</c> returns all instances.</param>
        /// <param name="instanceNumber">Instance number to match. <c>null</c> matches any.</param>
        /// <returns>QIDO-RS instance query object.</returns>
        public static DicomQidoRequest CreateInstanceQuery(
            string? studyInstanceUid = null,
            string? seriesInstanceUid = null,
            string? sopInstanceUid = null,
            string? instanceNumber = null)
        {
            var req = new DicomQidoRequest(DicomQueryRetrieveLevel.Image);
            req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, seriesInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.SOPInstanceUID, sopInstanceUid);
            req.Dataset.AddOrUpdate(DicomTag.InstanceNumber, instanceNumber);
            return req;
        }
        
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