// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using FellowOakDicom.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Serialises <see cref="DicomDataset"/> collections into HTTP response bodies.
    /// Shared by both <see cref="QidoResponseWriter"/> and <see cref="WadoResponseWriter"/>
    /// so that JSON and XML serialization logic lives in a single place.
    /// </summary>
    internal static class DicomMetadataSerializer
    {
        // ── JSON ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <paramref name="datasets"/> as a <c>application/dicom+json</c> array directly
        /// into the response body using <see cref="Utf8JsonWriter"/>, flushing after each dataset.
        /// Sets <c>Content-Type</c> on the response.
        /// </summary>
        internal static async Task WriteJsonAsync(
            HttpContext context,
            IEnumerable<DicomDataset> datasets,
            bool writeTagsAsKeywords,
            bool formatJsonIndented,
            CancellationToken cancellationToken)
        {
            context.Response.ContentType = "application/dicom+json";

            var converter = new DicomJsonConverter(writeTagsAsKeywords: writeTagsAsKeywords);
            var options = new JsonSerializerOptions { WriteIndented = formatJsonIndented };
            options.Converters.Add(converter);

            var writerOptions = new JsonWriterOptions { Indented = formatJsonIndented };
            await using var writer = new Utf8JsonWriter(context.Response.Body, writerOptions);

            writer.WriteStartArray();
            foreach (var ds in datasets)
            {
                converter.Write(writer, ds, options);
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Writes <paramref name="datasets"/> from an async enumerable as a
        /// <c>application/dicom+json</c> array directly into the response body, flushing after
        /// each dataset so the client receives data as it is produced.
        /// Sets <c>Content-Type</c> on the response.
        /// </summary>
        internal static async Task WriteJsonStreamingAsync(
            HttpContext context,
            IAsyncEnumerable<DicomDataset> datasets,
            bool writeTagsAsKeywords,
            bool formatJsonIndented,
            CancellationToken cancellationToken)
        {
            context.Response.ContentType = "application/dicom+json";

            var converter = new DicomJsonConverter(writeTagsAsKeywords: writeTagsAsKeywords);
            var options = new JsonSerializerOptions { WriteIndented = formatJsonIndented };
            options.Converters.Add(converter);

            var writerOptions = new JsonWriterOptions { Indented = formatJsonIndented };
            await using var writer = new Utf8JsonWriter(context.Response.Body, writerOptions);

            writer.WriteStartArray();
            await foreach (var ds in datasets.WithCancellation(cancellationToken))
            {
                converter.Write(writer, ds, options);
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);
        }

        // ── XML ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes each dataset as an individual <c>application/dicom+xml</c> multipart part
        /// directly into the response body, flushing after each part.
        /// When the enumerable is empty, a single part with an empty
        /// <c>NativeDicomModel</c> element is written (PS3.18 Section 10.4.1.1.2).
        /// Sets <c>Content-Type</c> on the response.
        /// </summary>
        internal static async Task WriteXmlMultipartAsync(
            HttpContext context,
            IEnumerable<DicomDataset> datasets,
            CancellationToken cancellationToken)
        {
            var boundary = Guid.NewGuid().ToString("N");
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom+xml\"; boundary={boundary}";

            bool any = false;
            foreach (var ds in datasets)
            {
                any = true;
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: application/dicom+xml\r\n\r\n", cancellationToken);
                await context.Response.WriteAsync(DicomXML.ConvertDicomToXML(ds), cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            if (!any)
            {
                // PS3.18 Section 10.4.1.1.2: empty result encoded as a single part
                // with an empty NativeDicomModel element.
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: application/dicom+xml\r\n\r\n", cancellationToken);
                await context.Response.WriteAsync(DicomXML.ConvertDicomToXML(new DicomDataset()), cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }

        /// <summary>
        /// Writes each dataset from an async enumerable as an individual
        /// <c>application/dicom+xml</c> multipart part directly into the response body,
        /// flushing after each part so the client receives data as it is produced.
        /// When the enumerable is empty, a single part with an empty
        /// <c>NativeDicomModel</c> element is written (PS3.18 Section 10.4.1.1.2).
        /// Sets <c>Content-Type</c> on the response.
        /// </summary>
        internal static async Task WriteXmlMultipartStreamingAsync(
            HttpContext context,
            IAsyncEnumerable<DicomDataset> datasets,
            CancellationToken cancellationToken)
        {
            var boundary = Guid.NewGuid().ToString("N");
            context.Response.ContentType =
                $"multipart/related; type=\"application/dicom+xml\"; boundary={boundary}";

            bool any = false;
            await foreach (var ds in datasets.WithCancellation(cancellationToken))
            {
                any = true;
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: application/dicom+xml\r\n\r\n", cancellationToken);
                await context.Response.WriteAsync(DicomXML.ConvertDicomToXML(ds), cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            if (!any)
            {
                // PS3.18 Section 10.4.1.1.2: empty result encoded as a single part
                // with an empty NativeDicomModel element.
                await context.Response.WriteAsync($"--{boundary}\r\n", cancellationToken);
                await context.Response.WriteAsync("Content-Type: application/dicom+xml\r\n\r\n", cancellationToken);
                await context.Response.WriteAsync(DicomXML.ConvertDicomToXML(new DicomDataset()), cancellationToken);
                await context.Response.WriteAsync("\r\n", cancellationToken);
            }

            await context.Response.WriteAsync($"--{boundary}--\r\n", cancellationToken);
        }
    }
}
