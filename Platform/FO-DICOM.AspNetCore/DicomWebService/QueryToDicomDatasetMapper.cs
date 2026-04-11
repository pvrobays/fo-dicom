using FellowOakDicom.DicomWeb;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Linq;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Translates an ASP.NET Core QIDO-RS query string into a <see cref="DicomQidoRequest"/>.
    /// </summary>
    /// <remarks>
    /// Handles:
    /// <list type="bullet">
    ///   <item>Reserved parameters: <c>fuzzymatching</c>, <c>limit</c>, <c>offset</c></item>
    ///   <item>Match attributes: by DICOM keyword (e.g. <c>PatientID</c>) or hex tag (e.g. <c>00100020</c>)</item>
    ///   <item>Include fields: <c>includefield=keyword|hextag</c>, dot-notation, bare SQ tags, and <c>includefield=all</c></item>
    ///   <item>Date range matching for DA/TM/DT VRs per PS3.4 C.2.2.2.5</item>
    ///   <item>UID list matching for UI VRs per PS3.18 Section 8.3.4.1</item>
    ///   <item>Dot-notation sequence attributes (e.g. <c>OtherPatientIDsSequence.PatientID=123</c>)</item>
    /// </list>
    /// </remarks>
    internal static class QueryToDicomDatasetMapper
    {
        private static readonly string[] _reservedQidoParameters = {
            "fuzzymatching",
            "limit",
            "offset"
        };

        /// <summary>
        /// Maps the given QIDO-RS query collection to a <see cref="DicomQidoRequest"/>.
        /// </summary>
        /// <param name="level">The query/retrieve level for the request.</param>
        /// <param name="query">The parsed query string from the HTTP request.</param>
        /// <returns>A fully populated <see cref="DicomQidoRequest"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a query parameter value is invalid (e.g. non-numeric limit/offset,
        /// unknown DICOM keyword, or malformed dot-notation path).
        /// </exception>
        internal static DicomQidoRequest Map(DicomQueryRetrieveLevel level, IQueryCollection query)
        {
            var isFuzzyMatching = ParseBoolean(query, "fuzzymatching");
            if (!TryParseInt(query, "limit", out var limit))
            {
                throw new InvalidOperationException("Invalid limit value");
            }

            if (!TryParseInt(query, "offset", out var offset))
            {
                throw new InvalidOperationException("Invalid offset value");
            }

            if (offset < 0)
            {
                throw new InvalidOperationException("Offset must be greater than or equal to 0");
            }

            var qidoRequestOptions = new DicomQidoRequestOptions(isFuzzyMatching, limit, offset);
            var dicomRequest = new DicomQidoRequest(level, qidoRequestOptions);
            var dataset = dicomRequest.Dataset;

            foreach ((string key, StringValues stringValues) in query)
            {
                if (_reservedQidoParameters.Contains(key))
                {
                    continue;
                }

                if (key.Equals("includefield", StringComparison.OrdinalIgnoreCase))
                {
                    var includeValues = stringValues.SelectMany(sv => sv.Split(',')).ToList();

                    // Per PS3.18 Section 8.3.4.3: "all" is mutually exclusive with other includefield values
                    var hasAll = includeValues.Any(v => v.Equals("all", StringComparison.OrdinalIgnoreCase));
                    if (hasAll)
                    {
                        if (includeValues.Count > 1)
                        {
                            throw new InvalidOperationException(
                                "includefield=all must not be combined with other includefield values (PS3.18 Section 8.3.4.3)");
                        }
                        dicomRequest.IncludeAllFields = true;
                        continue;
                    }

                    foreach (string value in includeValues)
                    {
                        if (value.Contains('.'))
                        {
                            // Dot notation: e.g. "00081115.00080060" or "OtherPatientIDsSequence.PatientID"
                            var path = ParseAttributePath(value);
                            AddNestedAttribute(dataset, path, new[] { string.Empty });
                            continue;
                        }

                        if (!DicomTag.TryParseByKeywordOrTag(value, out var includeTag))
                        {
                            throw new InvalidOperationException($"Could not map includefield '{value}' to a DICOM tag");
                        }
                        if (includeTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                        {
                            // SQ-typed tag without dot notation: add an empty sequence to the dataset
                            dataset.AddOrUpdate(new DicomSequence(includeTag));
                            continue;
                        }
                        dataset.AddOrUpdate(includeTag, string.Empty);
                    }

                    continue;
                }

                if (key.Contains('.'))
                {
                    // Dot notation: e.g. "00081115.00080060=CT" or "OtherPatientIDsSequence.PatientID=11235813"
                    var path = ParseAttributePath(key);
                    AddNestedAttribute(dataset, path, stringValues.ToArray()!);
                    continue;
                }

                if (!DicomTag.TryParseByKeywordOrTag(key, out var dicomTag))
                {
                    throw new InvalidOperationException($"Could not map query parameter '{key}' to a DICOM tag");
                }

                if (dicomTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                {
                    // Bare SQ-typed tag without dot notation: treat as include field (add empty sequence)
                    dataset.AddOrUpdate(new DicomSequence(dicomTag));
                    continue;
                }

                // Per PS3.4 C.2.2.2.5: DA/TM/DT tags support range syntax such as
                // "20130101-20131231", "-20131231" (open start), or "20130101-" (open end).
                // Detect the hyphen and store as a typed DicomDateRange so the dataset carries
                // proper range semantics rather than a raw string.
                if (IsDicomDateVr(dicomTag) && stringValues.ToString().Contains('-'))
                {
                    dataset.AddOrUpdate<DicomDateRange>(dicomTag, ParseDateRange(dicomTag, stringValues.ToString()));
                    continue;
                }

                // Per PS3.18 Section 8.3.4.1: UID list matching uses a comma-separated list of UIDs.
                // Split on comma and store each UID as a separate value so they are encoded as
                // backslash-delimited multi-value UI elements per PS3.4 C.2.2.2.2.
                if (IsUidVr(dicomTag))
                {
                    dataset.AddOrUpdate(dicomTag, stringValues.ToString().Split(','));
                    continue;
                }

                dataset.AddOrUpdate(dicomTag, stringValues.ToArray());
            }

            return dicomRequest;
        }

        /// <summary>
        /// Parses a dot-notation attribute path (e.g. "00081115.00080060" or
        /// "OtherPatientIDsSequence.PatientID") into an ordered array of DicomTags.
        /// Each segment can be a hex tag (8 hex digits) or a keyword.
        /// All segments except the last must be sequence (SQ) tags.
        /// </summary>
        private static DicomTag[] ParseAttributePath(string attributePath)
        {
            var segments = attributePath.Split('.');
            if (segments.Length < 2)
            {
                throw new InvalidOperationException(
                    $"Attribute path '{attributePath}' must contain at least two segments separated by '.'");
            }

            var tags = new DicomTag[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!DicomTag.TryParseByKeywordOrTag(segments[i], out var tag))
                {
                    throw new InvalidOperationException(
                        $"Could not parse segment '{segments[i]}' in attribute path '{attributePath}' as a DICOM tag");
                }

                // All segments except the last must be sequence tags
                if (i < segments.Length - 1 && !tag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
                {
                    throw new InvalidOperationException(
                        $"Segment '{segments[i]}' ({tag.DictionaryEntry.Keyword}) in attribute path '{attributePath}' is not a sequence tag");
                }

                tags[i] = tag;
            }

            return tags;
        }

        /// <summary>
        /// Adds a nested attribute to the dataset following a sequence path.
        /// For a path [SeqTagA, SeqTagB, LeafTag] with values ["CT"], this builds:
        ///   SeqTagA -&gt; DicomSequence containing one item -&gt;
        ///     SeqTagB -&gt; DicomSequence containing one item -&gt;
        ///       LeafTag = "CT"
        /// If a sequence already exists at any level, the leaf attribute is merged into
        /// the first existing sequence item rather than creating a duplicate sequence.
        /// </summary>
        private static void AddNestedAttribute(DicomDataset dataset, DicomTag[] path, string[] values)
        {
            var currentDataset = dataset;
            for (int i = 0; i < path.Length - 1; i++)
            {
                var seqTag = path[i];
                DicomSequence sequence;
                if (currentDataset.TryGetSequence(seqTag, out var existingSequence) && existingSequence.Items.Count > 0)
                {
                    sequence = existingSequence;
                }
                else
                {
                    sequence = new DicomSequence(seqTag, new DicomDataset().NotValidated());
                    currentDataset.AddOrUpdate(sequence);
                }

                currentDataset = sequence.Items[0];
            }

            var leafTag = path[path.Length - 1];
            if (leafTag.DictionaryEntry.ValueRepresentations.Contains(DicomVR.SQ))
            {
                currentDataset.AddOrUpdate(new DicomSequence(leafTag));
            }
            else if (values.Length == 1)
            {
                currentDataset.AddOrUpdate(leafTag, values[0]);
            }
            else
            {
                currentDataset.AddOrUpdate(leafTag, values);
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the primary VR of <paramref name="tag"/> is DA, TM, or DT —
        /// the three VRs for which DICOM defines range matching syntax (PS3.4 C.2.2.2.5).
        /// </summary>
        private static bool IsDicomDateVr(DicomTag tag)
        {
            var primaryVr = tag.DictionaryEntry.ValueRepresentations.FirstOrDefault();
            return primaryVr == DicomVR.DA || primaryVr == DicomVR.TM || primaryVr == DicomVR.DT;
        }

        /// <summary>
        /// Returns <c>true</c> when the primary VR of <paramref name="tag"/> is UI.
        /// UI tags support UID list matching per PS3.18 Section 8.3.4.1 and PS3.4 C.2.2.2.2.
        /// </summary>
        private static bool IsUidVr(DicomTag tag)
        {
            var primaryVr = tag.DictionaryEntry.ValueRepresentations.FirstOrDefault();
            return primaryVr == DicomVR.UI;
        }

        /// <summary>
        /// Parses a DICOM date/time range string (e.g. <c>"20130101-20131231"</c>,
        /// <c>"-20131231"</c>, <c>"20130101-"</c>) into a <see cref="DicomDateRange"/>.
        /// Delegates to <see cref="DicomDateElement.Get{T}"/> via a temporary dataset so that all
        /// VR-specific format strings (DA, TM, DT) are handled by the existing fo-dicom machinery.
        /// </summary>
        private static DicomDateRange ParseDateRange(DicomTag tag, string value)
        {
            var temp = new DicomDataset().NotValidated();
            temp.AddOrUpdate(tag, value);
            return temp.GetSingleValue<DicomDateRange>(tag);
        }

        private static bool TryParseInt(IQueryCollection query, string key, out int o)
        {
            if (!query.ContainsKey(key))
            {
                o = 0;
                return true;
            }

            return int.TryParse(query[key], out o);
        }

        private static bool ParseBoolean(IQueryCollection query, string key)
        {
            if (!query.ContainsKey(key))
            {
                return false;
            }

            return query[key].ToString().ToLowerInvariant() == "true";
        }
    }
}
