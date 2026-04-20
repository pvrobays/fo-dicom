// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.IO.Buffer;
using System.Collections.Generic;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Provides helpers for replacing bulk data elements in a <see cref="DicomDataset"/> with
    /// <see cref="BulkDataUriByteBuffer"/> references, as required by WADO-RS metadata
    /// responses (PS3.18 Section 10.4.1.1.2).
    /// <para>
    /// Bulk data VRs (OB, OD, OF, OL, OV, OW, UN) are replaced with a
    /// <c>BulkDataURI</c> reference so the JSON serializer emits <c>"BulkDataURI"</c>
    /// instead of <c>"InlineBinary"</c>. Fragment sequences (encapsulated pixel data)
    /// are always replaced, since the JSON serializer throws on <see cref="DicomFragmentSequence"/>.
    /// </para>
    /// </summary>
    internal static class DicomBulkDataHelper
    {
        /// <summary>
        /// The set of Value Representations that carry bulk data per DICOM PS3.18 Table F.2.3-1.
        /// </summary>
        private static readonly HashSet<DicomVR> BulkDataVRs = new HashSet<DicomVR>
        {
            DicomVR.OB,
            DicomVR.OD,
            DicomVR.OF,
            DicomVR.OL,
            DicomVR.OV,
            DicomVR.OW,
            DicomVR.UN
        };

        /// <summary>
        /// Returns <c>true</c> when the given <paramref name="vr"/> is one of the
        /// bulk data Value Representations (OB, OD, OF, OL, OV, OW, UN).
        /// </summary>
        internal static bool IsBulkDataVR(DicomVR vr)
            => BulkDataVRs.Contains(vr);

        /// <summary>
        /// Formats a <see cref="DicomTag"/> as an 8-character uppercase hexadecimal string
        /// suitable for use in bulk data URIs (e.g. <c>"7FE00010"</c>).
        /// </summary>
        internal static string FormatTagForUrl(DicomTag tag)
            => $"{tag.Group:X4}{tag.Element:X4}";

        /// <summary>
        /// Clones <paramref name="dataset"/> and replaces all bulk data elements with
        /// <see cref="BulkDataUriByteBuffer"/> references. The clone is shallow (element
        /// references are shared) except for elements that are replaced.
        /// <para>
        /// Elements whose <see cref="IByteBuffer.Size"/> is ≤ <paramref name="inlineThreshold"/>
        /// are left as-is and will be serialized as <c>"InlineBinary"</c>. Set the threshold
        /// to <c>0</c> to replace all bulk data elements (the PS3.18 default).
        /// </para>
        /// <para>
        /// <see cref="DicomFragmentSequence"/> instances are always replaced regardless of size,
        /// since the JSON serializer does not support fragment serialization.
        /// </para>
        /// </summary>
        /// <param name="dataset">The original dataset (not modified).</param>
        /// <param name="instanceBulkBaseUrl">
        /// The base URL for this instance, e.g.
        /// <c>/dicomweb/studies/{study}/series/{series}/instances/{sop}</c>.
        /// Bulk data URIs are formed as <c>{base}/bulk/{tag}</c> for top-level elements
        /// and <c>{base}/bulk/{seqTag}/{itemIndex}/{elementTag}</c> for nested elements.
        /// </param>
        /// <param name="inlineThreshold">
        /// Elements with <c>Buffer.Size ≤ inlineThreshold</c> are kept inline.
        /// Pass <c>0</c> to replace all bulk data elements.
        /// </param>
        /// <returns>A cloned dataset with bulk data elements replaced.</returns>
        internal static DicomDataset ReplaceBulkDataWithUris(
            DicomDataset dataset,
            string instanceBulkBaseUrl,
            long inlineThreshold)
        {
            var clone = dataset.Clone();
            ReplaceBulkDataInPlace(clone, instanceBulkBaseUrl, inlineThreshold, topLevel: true);
            return clone;
        }

        // ── Private implementation ────────────────────────────────────────────

        /// <summary>
        /// Walks all elements in <paramref name="dataset"/> and replaces bulk data elements
        /// in-place with <see cref="BulkDataUriByteBuffer"/> references. Recurses into sequences.
        /// </summary>
        /// <param name="dataset">The dataset to modify in-place (must be a clone).</param>
        /// <param name="bulkBaseUrl">
        /// When <paramref name="topLevel"/> is <c>true</c> this is the instance base URL and
        /// the helper appends <c>/bulk/{tag}</c>.
        /// When <paramref name="topLevel"/> is <c>false</c> this already contains the path up
        /// to (but not including) the element tag, and the helper appends <c>/{tag}</c>.
        /// </param>
        /// <param name="inlineThreshold">Inline-below threshold (bytes).</param>
        /// <param name="topLevel">
        /// <c>true</c> when processing the top-level dataset (URI form is <c>{base}/bulk/{tag}</c>);
        /// <c>false</c> when recursing into a sequence item (URI form is <c>{base}/{tag}</c>).
        /// </param>
        private static void ReplaceBulkDataInPlace(
            DicomDataset dataset,
            string bulkBaseUrl,
            long inlineThreshold,
            bool topLevel)
        {
            // Snapshot items so we can modify the dataset during iteration.
            var items = new List<DicomItem>();
            foreach (var item in dataset)
            {
                items.Add(item);
            }

            foreach (var item in items)
            {
                if (item is DicomSequence seq)
                {
                    // Recurse into each sequence item with a nested base URL.
                    var seqTagStr = FormatTagForUrl(seq.Tag);
                    for (int i = 0; i < seq.Items.Count; i++)
                    {
                        var nestedBase = $"{bulkBaseUrl}/{seqTagStr}/{i}";
                        ReplaceBulkDataInPlace(seq.Items[i], nestedBase, inlineThreshold, topLevel: false);
                    }
                    continue;
                }

                if (item is DicomFragmentSequence fragSeq)
                {
                    // Fragment sequences MUST always be replaced — the JSON serializer throws on them.
                    var tagStr = FormatTagForUrl(fragSeq.Tag);
                    var uri = topLevel
                        ? $"{bulkBaseUrl}/bulk/{tagStr}"
                        : $"{bulkBaseUrl}/{tagStr}";
                    var replacement = CreateBulkDataElement(
                        fragSeq.Tag, fragSeq.ValueRepresentation, new BulkDataUriByteBuffer(uri));
                    dataset.AddOrUpdate(replacement);
                    continue;
                }

                if (item is DicomElement element && IsBulkDataVR(element.ValueRepresentation))
                {
                    // Leave elements that are already a bulk data URI reference unchanged.
                    if (element.Buffer is IBulkDataUriByteBuffer)
                    {
                        continue;
                    }

                    // Leave elements that are small enough to inline.
                    if (inlineThreshold > 0 && element.Buffer != null && element.Buffer.Size <= inlineThreshold)
                    {
                        continue;
                    }

                    var tagStr = FormatTagForUrl(element.Tag);
                    var uri = topLevel
                        ? $"{bulkBaseUrl}/bulk/{tagStr}"
                        : $"{bulkBaseUrl}/{tagStr}";
                    var replacement = CreateBulkDataElement(
                        element.Tag, element.ValueRepresentation, new BulkDataUriByteBuffer(uri));
                    dataset.AddOrUpdate(replacement);
                }
            }
        }

        /// <summary>
        /// Creates a <see cref="DicomElement"/> of the correct concrete type for the given
        /// bulk data VR, wrapping the provided <paramref name="buffer"/>.
        /// </summary>
        private static DicomElement CreateBulkDataElement(DicomTag tag, DicomVR vr, IByteBuffer buffer)
        {
            if (vr == DicomVR.OB) return new DicomOtherByte(tag, buffer);
            if (vr == DicomVR.OD) return new DicomOtherDouble(tag, buffer);
            if (vr == DicomVR.OF) return new DicomOtherFloat(tag, buffer);
            if (vr == DicomVR.OL) return new DicomOtherLong(tag, buffer);
            if (vr == DicomVR.OV) return new DicomOtherVeryLong(tag, buffer);
            if (vr == DicomVR.OW) return new DicomOtherWord(tag, buffer);
            if (vr == DicomVR.UN) return new DicomUnknown(tag, buffer);
            // Fallback — should not be reached for bulk data VRs.
            return new DicomOtherByte(tag, buffer);
        }
    }
}
