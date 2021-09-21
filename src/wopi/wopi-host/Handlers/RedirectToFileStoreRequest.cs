using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost.Handlers
{
    internal sealed class RedirectToFileStoreRequest : WopiRequest
    {
        private readonly File _file;

        private RedirectToFileStoreRequest(File file, string accessToken)
            : base(accessToken, isWriteAccessRequired: false, demandsProof: false)
        {
            if (file.IsEmpty) throw new ArgumentNullException(nameof(file));

            _file = file;
        }

        internal static RedirectToFileStoreRequest With(File file, string accessToken) => new RedirectToFileStoreRequest(file, accessToken);

        protected override async Task HandleAsyncImpl(HttpContext httpContext, CancellationToken cancellationToken)
        {
            // This handler is tasked with generating an ephemeral link to our file storage (Azure blob store) location from 
            // where the target file can be directly downloaded (ie not having to be proxied through our servers) and then redirecting
            // the caller to it

            var fileRepository = httpContext.RequestServices.GetRequiredService<IFileRepository>();

            var fileMetadata = await fileRepository.GetMetadataAsync(_file, cancellationToken);

            if (fileMetadata.IsEmpty) throw new ApplicationException("The file metadata could not be found.  Please ensure the file is known to the application, or wait a few minutes for any database synchronisation activities to complete.  Alternatively report the issue to our support team so we can investigate if data has been lost as a result of a recent database restore operation.");

            if (FileStatus.Verified != fileMetadata.FileStatus) throw new ApplicationException($"The status of the file '{fileMetadata.FileStatus}' does not indicate it is yet safe to be shared with users.");

            var uri = await fileRepository.GeneratePublicEphemeralDownloadLink(fileMetadata, cancellationToken);

            if (uri is null) throw new ApplicationException($"Unable to generate an ephemeral download link for the file '{_file.Id}'");

            httpContext.Response.Redirect(uri.PathAndQuery, permanent: false);
        }
    }
}
