// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Writes STOW-RS Store Transaction responses (PS3.18 Section 10.5.1).
    /// <para>
    /// Maps <see cref="IDicomStowResponse"/> subtypes to HTTP status codes and serializes the
    /// STOW-RS Response Module as <c>application/dicom+xml</c> (default, per PS3.18) or
    /// <c>application/dicom+json</c> based on the request <c>Accept</c> header.
    /// </para>
    /// </summary>
    internal static class StowResponseWriter
    {
        // DICOM failure reason codes used for UID mismatch (PS3.4 Annex CC).
        internal const ushort FailureReasonMismatch = 0xC996;

        // PS3.18 Section 10.5.1: Warning header value for partial success.
        internal const string WarningHeaderValue =
            "299 - \"The STOW-RS Store Transaction (PS3.18 10.5) encountered instance-level failures.\"";

        /// <summary>
        /// Writes the HTTP status code and response body for the given STOW-RS response.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="response">The provider's response.</param>
        /// <param name="frameworkFailures">Framework-level failures (e.g. Study UID mismatches).</param>
        /// <param name="studyRetrieveUrl">
        /// Optional top-level Retrieve URL (0008,1190) for the study-level WADO-RS endpoint.
        /// PS3.18 Section 10.5.1 — SHOULD be present. <c>null</c> to omit.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        internal static async Task WriteAsync(
            HttpContext context,
            IDicomStowResponse response,
            IList<DicomStowInstanceResult> frameworkFailures,
            string? studyRetrieveUrl,
            CancellationToken cancellationToken)
        {
            if (response is DicomWebFailureResponse failure)
            {
                await DicomWebFailureWriter.WriteAsync(context, failure, cancellationToken);
                return;
            }

            IList<DicomStowInstanceResult> stored;
            IList<DicomStowInstanceResult> failed;

            if (response is DicomStowSuccessResponse successResp)
            {
                stored = successResp.StoredInstances;
                failed = new List<DicomStowInstanceResult>(frameworkFailures);
            }
            else if (response is DicomStowPartialSuccessResponse partialResp)
            {
                stored = partialResp.StoredInstances;
                // Merge provider failures with any framework-level failures (UID mismatches).
                var mergedFailed = new List<DicomStowInstanceResult>(partialResp.FailedInstances);
                mergedFailed.AddRange(frameworkFailures);
                failed = mergedFailed;
            }
            else
            {
                // Unknown response type — treat as service unavailable.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            // Determine final HTTP status:
            //   200 — all submitted instances stored (no failures of any kind)
            //   202 — partial success (some stored, some failed)
            //   409 — all instances failed (nothing stored successfully)
            bool anyStored = stored.Count > 0;
            bool anyFailed = failed.Count > 0;

            if (!anyStored && anyFailed)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
            }
            else if (anyFailed)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                // PS3.18 Section 10.5.1: SHOULD include Warning header for partial success.
                context.Response.Headers["Warning"] = WarningHeaderValue;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
            }

            // Content-negotiate: default XML, JSON if Accept header requests it.
            bool useJson = AcceptsJson(context);

            if (useJson)
            {
                context.Response.ContentType = "application/dicom+json; charset=utf-8";
                var json = BuildJsonResponse(studyRetrieveUrl, stored, failed);
                await context.Response.WriteAsync(json, Encoding.UTF8, cancellationToken);
            }
            else
            {
                context.Response.ContentType = "application/dicom+xml; charset=utf-8";
                var xml = BuildXmlResponse(studyRetrieveUrl, stored, failed);
                await context.Response.WriteAsync(xml, Encoding.UTF8, cancellationToken);
            }
        }

        // ── Content negotiation ────────────────────────────────────────────────

        private static bool AcceptsJson(HttpContext context)
        {
            var accept = context.Request.Headers["Accept"].ToString();
            if (string.IsNullOrEmpty(accept)) return false;
            return accept.IndexOf("application/dicom+json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── XML serialization (default, PS3.18 Section 10.5.1) ─────────────────

        /// <summary>
        /// Builds the STOW-RS Response Module as DICOM XML (PS3.18 Annex F.2).
        /// The root element is a NativeDicomModel containing:
        ///   - (0008,1190) RetrieveURL (study-level, when available)
        ///   - (0008,1199) ReferencedSOPSequence — one item per stored instance
        ///   - (0008,1198) FailedSOPSequence    — one item per failed instance
        /// </summary>
        private static string BuildXmlResponse(
            string? studyRetrieveUrl,
            IList<DicomStowInstanceResult> stored,
            IList<DicomStowInstanceResult> failed)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = false,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("NativeDicomModel");
                writer.WriteAttributeString("xml", "space", null, "preserve");

                // (0008,1190) top-level study RetrieveURL — PS3.18 Section 10.5.1
                if (studyRetrieveUrl != null)
                {
                    WriteXmlValue(writer, "00081190", "UR", studyRetrieveUrl);
                }

                if (stored.Count > 0)
                {
                    // (0008,1199) ReferencedSOPSequence
                    WriteXmlSequence(writer, "00081199", stored, includeRetrieveUrl: true);
                }

                if (failed.Count > 0)
                {
                    // (0008,1198) FailedSOPSequence
                    WriteXmlSequence(writer, "00081198", failed, includeRetrieveUrl: false);
                }

                writer.WriteEndElement(); // NativeDicomModel
                writer.WriteEndDocument();
            }

            return sb.ToString();
        }

        private static void WriteXmlSequence(
            XmlWriter writer,
            string tagHex,
            IList<DicomStowInstanceResult> items,
            bool includeRetrieveUrl)
        {
            writer.WriteStartElement("DicomAttribute");
            writer.WriteAttributeString("tag", tagHex);
            writer.WriteAttributeString("vr", "SQ");

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                writer.WriteStartElement("Item");
                writer.WriteAttributeString("number", (i + 1).ToString());

                // (0008,1150) ReferencedSOPClassUID
                WriteXmlValue(writer, "00081150", "UI", item.ReferencedSopClassUid);

                // (0008,1155) ReferencedSOPInstanceUID
                WriteXmlValue(writer, "00081155", "UI", item.ReferencedSopInstanceUid);

                // (0008,1190) RetrieveURL — only in success sequence
                if (includeRetrieveUrl && item.RetrieveUrl != null)
                {
                    WriteXmlValue(writer, "00081190", "UR", item.RetrieveUrl);
                }

                // (0008,1197) FailureReason — only in failure sequence
                if (item.FailureReason.HasValue)
                {
                    WriteXmlValue(writer, "00081197", "US",
                        item.FailureReason.Value.ToString());
                }

                writer.WriteEndElement(); // Item
            }

            writer.WriteEndElement(); // DicomAttribute
        }

        private static void WriteXmlValue(XmlWriter writer, string tagHex, string vr, string value)
        {
            writer.WriteStartElement("DicomAttribute");
            writer.WriteAttributeString("tag", tagHex);
            writer.WriteAttributeString("vr", vr);
            writer.WriteStartElement("Value");
            writer.WriteAttributeString("number", "1");
            writer.WriteString(value);
            writer.WriteEndElement(); // Value
            writer.WriteEndElement(); // DicomAttribute
        }

        // ── JSON serialization ──────────────────────────────────────────────────

        /// <summary>
        /// Builds the STOW-RS Response Module as DICOM JSON (PS3.18 Annex F.2.3).
        /// </summary>
        private static string BuildJsonResponse(
            string? studyRetrieveUrl,
            IList<DicomStowInstanceResult> stored,
            IList<DicomStowInstanceResult> failed)
        {
            var sb = new StringBuilder();
            sb.Append('{');

            bool needsComma = false;

            // (0008,1190) top-level study RetrieveURL — PS3.18 Section 10.5.1
            if (studyRetrieveUrl != null)
            {
                AppendJsonValue(sb, "00081190", "UR", studyRetrieveUrl);
                needsComma = true;
            }

            if (stored.Count > 0)
            {
                if (needsComma) sb.Append(',');
                // "00081199": { "vr": "SQ", "Value": [...] }
                AppendJsonSequence(sb, "00081199", stored, includeRetrieveUrl: true);
                needsComma = true;
            }

            if (failed.Count > 0)
            {
                if (needsComma) sb.Append(',');
                // "00081198": { "vr": "SQ", "Value": [...] }
                AppendJsonSequence(sb, "00081198", failed, includeRetrieveUrl: false);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendJsonSequence(
            StringBuilder sb,
            string tagHex,
            IList<DicomStowInstanceResult> items,
            bool includeRetrieveUrl)
        {
            sb.Append('"').Append(tagHex).Append('"');
            sb.Append(":{\"vr\":\"SQ\",\"Value\":[");

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var item = items[i];
                sb.Append('{');

                // "00081150": { "vr": "UI", "Value": ["..."] }
                AppendJsonValue(sb, "00081150", "UI", item.ReferencedSopClassUid);
                sb.Append(',');

                // "00081155": { "vr": "UI", "Value": ["..."] }
                AppendJsonValue(sb, "00081155", "UI", item.ReferencedSopInstanceUid);

                if (includeRetrieveUrl && item.RetrieveUrl != null)
                {
                    sb.Append(',');
                    AppendJsonValue(sb, "00081190", "UR", item.RetrieveUrl);
                }

                if (item.FailureReason.HasValue)
                {
                    sb.Append(',');
                    // US values are numeric in DICOM JSON
                    sb.Append('"').Append("00081197").Append('"');
                    sb.Append(":{\"vr\":\"US\",\"Value\":[");
                    sb.Append(item.FailureReason.Value);
                    sb.Append("]}");
                }

                sb.Append('}');
            }

            sb.Append("]}");
        }

        private static void AppendJsonValue(StringBuilder sb, string tagHex, string vr, string value)
        {
            sb.Append('"').Append(tagHex).Append('"');
            sb.Append(":{\"vr\":\"").Append(vr).Append("\",\"Value\":[\"");
            // Basic JSON string escaping for the value.
            sb.Append(JsonEscape(value));
            sb.Append("\"]}");
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
