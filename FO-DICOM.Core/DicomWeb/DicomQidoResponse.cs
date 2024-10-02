using System;
using System.Collections.Generic;

namespace FellowOakDicom.DicomWeb
{
    public interface IDicomQidoResponse { }
    
    public class DicomQidoSuccessResponse : IDicomQidoResponse
    {
        
        #region CONSTRUCTORS
        public DicomQidoSuccessResponse()
        {
            
        }
        
        #endregion
        
        #region PROPERTIES
        
        /// <summary>
        /// The results of the QIDO-RS request
        /// </summary>
        public IList<DicomDataset> Results { get; set; } = new List<DicomDataset>();
        
        /// <summary>
        /// Whether the results returned supported fuzzy matching.
        /// If request had fuzzy matching options, and the server does not support it, will return Warning header:
        /// Warning: 299 {SERVICE}: "The fuzzymatching parameter is not supported. Only literal matching has been performed."
        /// </summary>
        public bool IsFuzzyMatchingSupported { get; set; }
        
        /// <summary>
        /// Whether the server reached the maximum number of results, and the results are incomplete
        /// If thje server reached the maximum number of results, will return Warning header:
        /// Warning: 299 {SERVICE}: "The number of results exceeded the maximum supported by the server. Additional results can be requested."
        /// </summary>
        public bool IsServerMaximumResultsReached { get; set; }
        
        #endregion
        
        #region METHODS
        
        public void AddResult(DicomDataset dataset) => Results.Add(dataset);

        #endregion
    }
    
    public class DicomQidoFailureResponse: IDicomQidoResponse { }

    public class DicomQidoBadRequestResponse : DicomQidoFailureResponse
    {
        public string? Reason { get; set; }

        public DicomQidoBadRequestResponse(string? reason = null)
        {
            Reason = reason;
        }
    }
    
    public class DicomQidoUnauthorizedResponse : DicomQidoFailureResponse { }
    
    public class DicomQidoForbiddenResponse : DicomQidoFailureResponse { }
    
    public class DicomQidoRequestTooBroadResponse : DicomQidoFailureResponse { }

    public class DicomQidoUnavailableResponse : DicomQidoFailureResponse
    {
        public string? Reason { get; set; }

        public DicomQidoUnavailableResponse(string? reason = null)
        {
            Reason = reason;
        }
    }
    
    public class DicomQidoNotImplementedResponse : DicomQidoFailureResponse { }
}