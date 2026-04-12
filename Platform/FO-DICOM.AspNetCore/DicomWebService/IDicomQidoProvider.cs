using FellowOakDicom.DicomWeb;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebService
{
    public interface IDicomQidoProvider
    {
        /// <summary>
        /// Handles a QIDO-RS search request.
        /// </summary>
        /// <param name="request">
        /// The parsed QIDO request, including the query dataset (match constraints and include
        /// fields), query/retrieve level, and options (limit, offset, fuzzy matching).
        /// </param>
        /// <param name="httpContext">
        /// The full ASP.NET Core HTTP context for the current request. Use this to inspect
        /// authentication (<c>httpContext.User</c> for JWT claims,
        /// <c>httpContext.Connection.ClientCertificate</c> for mTLS), request headers,
        /// or resolve scoped services via <c>httpContext.RequestServices</c>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token tied to the HTTP request lifetime.</param>
        /// <returns>
        /// An <see cref="IDicomQidoResponse"/> — typically a <see cref="DicomQidoSuccessResponse"/>
        /// with result datasets, or one of the typed failure responses.
        /// </returns>
        Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request, HttpContext httpContext, CancellationToken cancellationToken);
    }
}