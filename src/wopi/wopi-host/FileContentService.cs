using FutureNHS.WOPIHost.Azure;
using FutureNHS.WOPIHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost
{
    public interface IFileContentService
    {
        /// <summary>
        /// Tasked with retrieving an ephemeral endpoint from which the contents of the file can be directly downloaded without the 
        /// need for it to be proxied through our file server.  The returned url will be formed such that traffic from outside of the 
        /// virtual network will be routed through our application gateway
        /// </summary>
        /// <param name="fileMetadata">The metadata for the file to which the ephemeral endpoint will be explicitly tied</param>
        /// <param name="cancellationToken"></param>
        /// <returns>In the success case, the url of the endpoint else a null value</returns>
        Task<Uri> GenerateEphemeralDownloadLink(FileMetadata file, CancellationToken cancellationToken);
    }

    public sealed class FileContentService : IFileContentService
    {
        private readonly IAzureBlobStoreClient _azureBlobStoreClient;
        private readonly ILogger<FileContentService>? _logger;

        private readonly string _blobContainerName;

        public FileContentService(IAzureBlobStoreClient azureBlobStoreClient, IOptionsSnapshot<AzurePlatformConfiguration> azurePlatformConfiguration, ILogger<FileContentService>? logger)
        {
            _logger = logger;

            _azureBlobStoreClient = azureBlobStoreClient ?? throw new ArgumentNullException(nameof(azureBlobStoreClient));

            if (azurePlatformConfiguration?.Value is null) throw new ArgumentNullException(nameof(azurePlatformConfiguration));

            var blobContainerName = azurePlatformConfiguration.Value.AzureBlobStorage?.ContainerName;

            if (string.IsNullOrWhiteSpace(blobContainerName)) throw new ApplicationException("The files blob container name is not set in the configuration");

            _blobContainerName = blobContainerName;
        }

        async Task<Uri> IFileContentService.GenerateEphemeralDownloadLink(FileMetadata fileMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fileMetadata is null || fileMetadata.IsEmpty) throw new ArgumentNullException(nameof(fileMetadata));

            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.BlobName));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.Version));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.Name));

            var blobUri = await _azureBlobStoreClient.GenerateEphemeralDownloadLink(_blobContainerName, fileMetadata.BlobName, fileMetadata.Version, fileMetadata.Name, cancellationToken);

            Debug.Assert(blobUri.IsAbsoluteUri);

            // The blob uri links direct to the blob store using the azure assigned DNS entry for the host name, however, we want to direct all 
            // traffic through our application gateway therefore we need to amend the url

            var uriBuilder = new UriBuilder
            {
                Path = string.Concat("gateway/media", blobUri.LocalPath),
                Query = blobUri.Query
            };

            return uriBuilder.Uri;
        }
    }
}
