using FellowOakDicom.DicomWeb;
using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.AspNetCore.DicomWebServer
{
    public interface IDicomQidoProvider
    {
        Task<IDicomQidoResponse> OnQidoRequestAsync(DicomQidoRequest request /*TODO PJ: Add some context*/, CancellationToken cancellationToken);
    }
}