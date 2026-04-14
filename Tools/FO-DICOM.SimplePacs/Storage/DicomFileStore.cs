// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.SimplePacs.Storage
{
    /// <summary>
    /// Reads and writes DICOM files on disk using the layout:
    ///   {StorageRoot}/{StudyInstanceUID}/{SopInstanceUID}.dcm
    /// </summary>
    internal sealed class DicomFileStore
    {
        private readonly string _storageRoot;

        internal DicomFileStore(string storageRoot)
        {
            _storageRoot = storageRoot;
        }

        /// <summary>
        /// Returns the absolute path for the given instance.
        /// </summary>
        internal string GetFilePath(string studyInstanceUid, string sopInstanceUid)
            => Path.Combine(_storageRoot, studyInstanceUid, sopInstanceUid + ".dcm");

        /// <summary>
        /// Saves a DICOM file to disk. Creates the parent directory if needed.
        /// Overwrites any existing file with the same SOP Instance UID.
        /// </summary>
        internal async Task SaveAsync(
            DicomFile file,
            string studyInstanceUid,
            string sopInstanceUid,
            CancellationToken cancellationToken)
        {
            var path = GetFilePath(studyInstanceUid, sopInstanceUid);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await file.SaveAsync(path).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a stream over a stored DICOM file. The caller is responsible for disposing.
        /// Returns <c>null</c> if the file does not exist.
        /// </summary>
        internal FileStream? OpenRead(string studyInstanceUid, string sopInstanceUid)
        {
            var path = GetFilePath(studyInstanceUid, sopInstanceUid);
            if (!File.Exists(path)) return null;
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);
        }

        /// <summary>
        /// Loads and parses a DICOM file from disk.
        /// Returns <c>null</c> if the file does not exist.
        /// </summary>
        internal async Task<DicomFile?> LoadAsync(
            string studyInstanceUid,
            string sopInstanceUid,
            CancellationToken cancellationToken)
        {
            var path = GetFilePath(studyInstanceUid, sopInstanceUid);
            if (!File.Exists(path)) return null;
            return await DicomFile.OpenAsync(path).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a stored DICOM file. No-op if the file does not exist.
        /// </summary>
        internal void Delete(string studyInstanceUid, string sopInstanceUid)
        {
            var path = GetFilePath(studyInstanceUid, sopInstanceUid);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
