// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;
using System.IO;

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Marker interface for responses to WADO-RS requests.
    /// Returned by <see cref="FellowOakDicom.AspNetCore.DicomWebService.IDicomWadoProvider"/> methods.
    /// </summary>
    public interface IDicomWadoResponse { }

    // ── Instance responses ────────────────────────────────────────────────────

    /// <summary>
    /// A WADO-RS instance retrieval response carrying <see cref="DicomFile"/> objects.
    /// The framework serializes each file as a part in a
    /// <c>multipart/related; type="application/dicom"</c> response body (PS3.18 Section 10.4).
    /// </summary>
    public class DicomWadoInstancesResponse : IDicomWadoResponse
    {
        /// <summary>The DICOM instances to return to the client.</summary>
        public IList<DicomFile> Results { get; }

        public DicomWadoInstancesResponse(IList<DicomFile> results)
        {
            Results = results;
        }
    }

    /// <summary>
    /// A single raw (pre-encoded) DICOM instance part, used in
    /// <see cref="DicomWadoRawInstancesResponse"/> when the caller wants to supply
    /// already-encoded Part 10 bytes rather than <see cref="DicomFile"/> objects.
    /// </summary>
    public class DicomWadoRawInstance
    {
        /// <summary>
        /// A stream containing the raw Part 10 encoded bytes for one instance.
        /// The framework reads and forwards this stream verbatim into the multipart part body.
        /// </summary>
        public Stream Data { get; }

        /// <summary>
        /// The Transfer Syntax UID for this instance (e.g. <c>1.2.840.10008.1.2.1</c>
        /// for Explicit VR Little Endian), included in the part's <c>Content-Type</c> parameter.
        /// May be <c>null</c> if the transfer syntax is unknown or not relevant.
        /// </summary>
        public string TransferSyntaxUid { get; }

        public DicomWadoRawInstance(Stream data, string transferSyntaxUid = null)
        {
            Data = data;
            TransferSyntaxUid = transferSyntaxUid;
        }
    }

    /// <summary>
    /// A WADO-RS instance retrieval response carrying pre-encoded raw byte streams.
    /// Use this when the provider already has Part 10 bytes and does not need to
    /// deserialize them into <see cref="DicomFile"/> objects.
    /// The framework writes each stream verbatim into a multipart part body.
    /// </summary>
    public class DicomWadoRawInstancesResponse : IDicomWadoResponse
    {
        /// <summary>The raw Part 10 encoded instance streams to return to the client.</summary>
        public IList<DicomWadoRawInstance> Results { get; }

        public DicomWadoRawInstancesResponse(IList<DicomWadoRawInstance> results)
        {
            Results = results;
        }
    }

    /// <summary>
    /// A WADO-RS instance retrieval response backed by an <see cref="IAsyncEnumerable{T}"/> of
    /// <see cref="DicomFile"/> objects, enabling streaming of large result sets without
    /// buffering all instances in memory simultaneously.
    /// </summary>
    public class DicomWadoAsyncInstancesResponse : IDicomWadoResponse
    {
        /// <summary>The async sequence of DICOM instances to stream to the client.</summary>
        public IAsyncEnumerable<DicomFile> Results { get; }

        public DicomWadoAsyncInstancesResponse(IAsyncEnumerable<DicomFile> results)
        {
            Results = results;
        }
    }

    /// <summary>
    /// A WADO-RS instance retrieval response backed by an <see cref="IAsyncEnumerable{T}"/> of
    /// <see cref="DicomWadoRawInstance"/>, enabling streaming of pre-encoded Part 10 bytes
    /// without buffering the entire study or series in memory.
    /// </summary>
    public class DicomWadoAsyncRawInstancesResponse : IDicomWadoResponse
    {
        /// <summary>The async sequence of raw Part 10 encoded instance streams.</summary>
        public IAsyncEnumerable<DicomWadoRawInstance> Results { get; }

        public DicomWadoAsyncRawInstancesResponse(IAsyncEnumerable<DicomWadoRawInstance> results)
        {
            Results = results;
        }
    }

    // ── Metadata responses ────────────────────────────────────────────────────

    /// <summary>
    /// A WADO-RS metadata retrieval response carrying a list of <see cref="DicomDataset"/> objects
    /// (one per instance, with bulk data excluded). The framework serializes these as
    /// <c>application/dicom+json</c> or <c>multipart/related; type="application/dicom+xml"</c>
    /// (PS3.18 Section 10.4.1.1.2).
    /// </summary>
    public class DicomWadoMetadataResponse : IDicomWadoResponse
    {
        /// <summary>The instance metadata datasets to return to the client.</summary>
        public IList<DicomDataset> Results { get; }

        public DicomWadoMetadataResponse(IList<DicomDataset> results)
        {
            Results = results;
        }
    }

    /// <summary>
    /// A WADO-RS metadata retrieval response backed by an <see cref="IAsyncEnumerable{T}"/> of
    /// <see cref="DicomDataset"/>, enabling streaming of metadata for large studies without
    /// buffering all datasets in memory.
    /// </summary>
    public class DicomWadoAsyncMetadataResponse : IDicomWadoResponse
    {
        /// <summary>The async sequence of instance metadata datasets.</summary>
        public IAsyncEnumerable<DicomDataset> Results { get; }

        public DicomWadoAsyncMetadataResponse(IAsyncEnumerable<DicomDataset> results)
        {
            Results = results;
        }
    }
}
