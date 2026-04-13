// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.IO.Buffer;
using System.Collections.Generic;
using System.IO;

namespace FellowOakDicom.DicomWeb
{
    /// <summary>
    /// Common base interface for all responses to WADO-RS requests.
    /// </summary>
    public interface IDicomWadoResponse { }

    /// <summary>
    /// Marker interface for responses to WADO-RS instance retrieval requests
    /// (<see cref="FellowOakDicom.AspNetCore.DicomWebService.IDicomWadoProvider.OnRetrieveInstancesAsync"/>).
    /// Implemented by instance response types and all failure responses.
    /// </summary>
    public interface IDicomWadoInstanceResponse : IDicomWadoResponse { }

    /// <summary>
    /// Marker interface for responses to WADO-RS metadata retrieval requests
    /// (<see cref="FellowOakDicom.AspNetCore.DicomWebService.IDicomWadoProvider.OnRetrieveMetadataAsync"/>).
    /// Implemented by metadata response types and all failure responses.
    /// </summary>
    public interface IDicomWadoMetadataResponse : IDicomWadoResponse { }

    /// <summary>
    /// Marker interface for responses to WADO-RS frame retrieval requests
    /// (PS3.18 Section 10.4.1.1.4).
    /// Implemented by frame response types and all failure responses.
    /// </summary>
    public interface IDicomWadoFrameResponse : IDicomWadoResponse { }

    // ── Instance responses ────────────────────────────────────────────────────

    /// <summary>
    /// A WADO-RS instance retrieval response carrying <see cref="DicomFile"/> objects.
    /// The framework serializes each file as a part in a
    /// <c>multipart/related; type="application/dicom"</c> response body (PS3.18 Section 10.4).
    /// </summary>
    public class DicomWadoInstancesResponse : IDicomWadoInstanceResponse
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
        public string? TransferSyntaxUid { get; }

        /// <summary>
        /// The Study Instance UID of this instance, used by the framework to build the
        /// <c>Content-Location</c> header for this multipart part (PS3.18 Section 10.4.1.1).
        /// When <c>null</c>, no <c>Content-Location</c> header is emitted for this part.
        /// </summary>
        public string? StudyInstanceUid { get; }

        /// <summary>
        /// The Series Instance UID of this instance, used together with
        /// <see cref="StudyInstanceUid"/> and <see cref="SopInstanceUid"/> to build the
        /// <c>Content-Location</c> header. When <c>null</c>, no header is emitted.
        /// </summary>
        public string? SeriesInstanceUid { get; }

        /// <summary>
        /// The SOP Instance UID of this instance, used together with
        /// <see cref="StudyInstanceUid"/> and <see cref="SeriesInstanceUid"/> to build the
        /// <c>Content-Location</c> header. When <c>null</c>, no header is emitted.
        /// </summary>
        public string? SopInstanceUid { get; }

        /// <summary>
        /// Creates a raw instance part with optional transfer syntax and no UID context.
        /// The framework will not emit a <c>Content-Location</c> header for this part.
        /// </summary>
        public DicomWadoRawInstance(Stream data, string? transferSyntaxUid = null)
        {
            Data = data;
            TransferSyntaxUid = transferSyntaxUid;
        }

        /// <summary>
        /// Creates a raw instance part with transfer syntax and the three UIDs needed for
        /// the framework to emit a <c>Content-Location</c> header for this multipart part
        /// (PS3.18 Section 10.4.1.1).
        /// </summary>
        /// <param name="data">Raw Part 10 encoded bytes for this instance.</param>
        /// <param name="transferSyntaxUid">Transfer Syntax UID, or <c>null</c> if unknown.</param>
        /// <param name="studyInstanceUid">Study Instance UID.</param>
        /// <param name="seriesInstanceUid">Series Instance UID.</param>
        /// <param name="sopInstanceUid">SOP Instance UID.</param>
        public DicomWadoRawInstance(
            Stream data,
            string? transferSyntaxUid,
            string? studyInstanceUid,
            string? seriesInstanceUid,
            string? sopInstanceUid)
        {
            Data = data;
            TransferSyntaxUid = transferSyntaxUid;
            StudyInstanceUid = studyInstanceUid;
            SeriesInstanceUid = seriesInstanceUid;
            SopInstanceUid = sopInstanceUid;
        }
    }

    /// <summary>
    /// A WADO-RS instance retrieval response carrying pre-encoded raw byte streams.
    /// Use this when the provider already has Part 10 bytes and does not need to
    /// deserialize them into <see cref="DicomFile"/> objects.
    /// The framework writes each stream verbatim into a multipart part body.
    /// </summary>
    public class DicomWadoRawInstancesResponse : IDicomWadoInstanceResponse
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
    public class DicomWadoAsyncInstancesResponse : IDicomWadoInstanceResponse
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
    public class DicomWadoAsyncRawInstancesResponse : IDicomWadoInstanceResponse
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
    public class DicomWadoMetadataResponse : IDicomWadoMetadataResponse
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
    public class DicomWadoAsyncMetadataResponse : IDicomWadoMetadataResponse
    {
        /// <summary>The async sequence of instance metadata datasets.</summary>
        public IAsyncEnumerable<DicomDataset> Results { get; }

        public DicomWadoAsyncMetadataResponse(IAsyncEnumerable<DicomDataset> results)
        {
            Results = results;
        }
    }

    // ── Frame responses ───────────────────────────────────────────────────────

    /// <summary>
    /// The pixel data for a single DICOM frame within a WADO-RS frame retrieval response
    /// (PS3.18 Section 10.4.1.1.4).
    /// <para>
    /// The <see cref="MediaType"/> is the MIME type that applies to <see cref="Data"/> —
    /// e.g. <c>application/octet-stream</c> for uncompressed pixels, <c>image/jpeg</c> for
    /// JPEG-compressed, <c>image/jp2</c> for JPEG 2000, etc.
    /// </para>
    /// </summary>
    public class DicomWadoFrameData
    {
        /// <summary>
        /// The raw pixel bytes for this frame.
        /// For uncompressed syntaxes this is the raw raster sample data; for compressed
        /// syntaxes this is the native bitstream (JPEG, JPEG 2000, etc.) as returned by
        /// <see cref="FellowOakDicom.Imaging.DicomPixelData.GetFrame(int)"/>.
        /// </summary>
        public IByteBuffer Data { get; }

        /// <summary>
        /// The MIME media type for <see cref="Data"/>, used as the <c>Content-Type</c>
        /// of the multipart part that contains this frame. Examples:
        /// <list type="bullet">
        ///   <item><c>application/octet-stream</c> — uncompressed raw pixels</item>
        ///   <item><c>image/jpeg</c> — JPEG (any process)</item>
        ///   <item><c>image/jp2</c> — JPEG 2000</item>
        ///   <item><c>image/x-jls</c> — JPEG-LS</item>
        ///   <item><c>image/jphc</c> — High-Throughput JPEG 2000 (HTJ2K)</item>
        /// </list>
        /// </summary>
        public string MediaType { get; }

        /// <summary>
        /// The 1-based frame number within the instance (as specified in the request URL).
        /// </summary>
        public int FrameNumber { get; }

        /// <summary>
        /// Creates a frame data object.
        /// </summary>
        /// <param name="data">Raw pixel bytes for the frame.</param>
        /// <param name="mediaType">MIME type for the frame bytes (e.g. <c>"image/jpeg"</c>).</param>
        /// <param name="frameNumber">1-based frame number.</param>
        public DicomWadoFrameData(IByteBuffer data, string mediaType, int frameNumber)
        {
            Data = data;
            MediaType = mediaType;
            FrameNumber = frameNumber;
        }
    }

    /// <summary>
    /// A WADO-RS frame retrieval response carrying a list of per-frame pixel data objects.
    /// The framework serializes each frame as a part in a
    /// <c>multipart/related; type="&lt;mediaType&gt;"</c> response body
    /// (PS3.18 Section 10.4.1.1.4).
    /// </summary>
    public class DicomWadoFramesResponse : IDicomWadoFrameResponse
    {
        /// <summary>The frame data to return to the client (in request order).</summary>
        public IList<DicomWadoFrameData> Results { get; }

        public DicomWadoFramesResponse(IList<DicomWadoFrameData> results)
        {
            Results = results;
        }
    }
}
