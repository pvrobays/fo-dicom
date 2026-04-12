// Copyright (c) 2012-2025 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).
#nullable disable

using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    /// <summary>
    /// Defines the contract for handling QIDO-RS search requests at each retrieval level.
    /// Implemented by <see cref="DicomWebService"/>.
    /// </summary>
    public interface IDicomWebService
    {
        /// <summary>Handles a QIDO-RS All Studies search (<c>GET …/studies</c>).</summary>
        Task HandleQidoStudiesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Series search.
        /// <list type="bullet">
        ///   <item><c>GET …/series</c> — all series (no route values needed)</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series</c> — series within a study</item>
        /// </list>
        /// The study scope is extracted automatically from the <c>studyInstanceUID</c> route value
        /// when present in <see cref="HttpRequest.RouteValues"/>.
        /// </summary>
        Task HandleQidoSeriesRequestAsync(HttpContext context);

        /// <summary>
        /// Handles a QIDO-RS Instances search.
        /// <list type="bullet">
        ///   <item><c>GET …/instances</c> — all instances</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/instances</c> — instances within a study</item>
        ///   <item><c>GET …/studies/{studyInstanceUID}/series/{seriesInstanceUID}/instances</c> — instances within a series</item>
        /// </list>
        /// The study and series scope are extracted automatically from the route values
        /// <c>studyInstanceUID</c> and <c>seriesInstanceUID</c> when present.
        /// </summary>
        Task HandleQidoInstancesRequestAsync(HttpContext context);
    }
}
