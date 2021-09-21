using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FutureNHS.WOPIHost.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost.PlatformHelpers
{  
    public interface IAzureBlobStoreClient
    {
        Task<BlobDownloadDetails> FetchBlobAndWriteToStream(string containerName, string blobName, string blobVersion, string contentHash, Stream streamToWriteTo, CancellationToken cancellationToken);

        Task<Uri> GenerateEphemeralDownloadLink(string containerName, string blobName, string blobVersion, string publicFacingBlobName, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Helper object that can be used to access Azure Blob Storage to perform common tasks
    /// </summary>
    /// <remarks>
    /// The identity being used to access Azure will need to have the appropriate
    /// permissions/role assigned to read content out of the target blob storage account/container combo.
    /// <b>Identity for authentication is discovered in the following order:</b>
    /// <list type="bullet">
    /// <item>Environment Vars</item>
    /// <item>Managed Identity if running in Azure</item>
    /// <item>Visual Studio (tools:options:azure service authentication:account selection)</item>
    /// <item>Azure CLI</item>
    /// <item>Azure Powershell</item>
    /// <item>Interactive (triggered with browser login)</item>
    /// </list>
    /// </remarks>
    public sealed class AzureBlobStoreClient : IAzureBlobStoreClient
    {
        const int TOKEN_SAS_TIMEOUT_IN_MINUTES = 40;                 // Aligns with authentication cookie timeout policy for which we have an NFR

        private readonly IMemoryCache _memoryCache;
        private readonly ISystemClock _clock;
        private readonly ILogger<AzureBlobStoreClient>? _logger;

        private readonly Uri _primaryServiceUrl;
        private readonly Uri _geoRedundantServiceUrl;

        public AzureBlobStoreClient(Uri primaryServiceUrl, Uri geoRedundantServiceUrl, IMemoryCache memoryCache, ISystemClock clock, ILogger<AzureBlobStoreClient>? logger)
        {
            _primaryServiceUrl = primaryServiceUrl             ?? throw new ArgumentNullException(nameof(primaryServiceUrl));
            _geoRedundantServiceUrl = geoRedundantServiceUrl   ?? throw new ArgumentNullException(nameof(geoRedundantServiceUrl));
            _memoryCache = memoryCache                         ?? throw new ArgumentNullException(nameof(memoryCache));
            _clock = clock                                     ?? throw new ArgumentNullException(nameof(clock));

            _logger = logger;
        }

        private static bool IsSuccessStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;

        private static BlobClientOptions GetBlobClientOptions(Uri geoRedundantServiceUrl)
        {
            var blobClientOptions = new BlobClientOptions { GeoRedundantSecondaryUri = geoRedundantServiceUrl };

            // TODO - Set retry options in line with NFRs once they have been established with the client

            blobClientOptions.Retry.Delay = TimeSpan.FromMilliseconds(800);
            blobClientOptions.Retry.MaxDelay = TimeSpan.FromMinutes(1);
            blobClientOptions.Retry.MaxRetries = 5;
            blobClientOptions.Retry.Mode = Azure.Core.RetryMode.Exponential;
            blobClientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(100);

            blobClientOptions.Diagnostics.IsDistributedTracingEnabled = true;
            blobClientOptions.Diagnostics.IsLoggingContentEnabled = false;
            blobClientOptions.Diagnostics.IsLoggingEnabled = true;
            blobClientOptions.Diagnostics.IsTelemetryEnabled = true;

            return blobClientOptions;
        }

        async Task<BlobDownloadDetails> IAzureBlobStoreClient.FetchBlobAndWriteToStream(string containerName, string blobName, string blobVersion, string contentHash, Stream streamToWriteTo, CancellationToken cancellationToken)
        {
            // https://docs.microsoft.com/en-us/azure/storage/common/storage-auth-aad-msi
            // https://docs.microsoft.com/en-gb/dotnet/api/overview/azure/identity-readme

            if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentNullException(nameof(containerName));
            if (string.IsNullOrWhiteSpace(blobName)) throw new ArgumentNullException(nameof(blobName));
            if (string.IsNullOrWhiteSpace(blobVersion)) throw new ArgumentNullException(nameof(blobVersion));
            if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentNullException(nameof(contentHash));

            if (streamToWriteTo is null) throw new ArgumentNullException(nameof(streamToWriteTo));

            cancellationToken.ThrowIfCancellationRequested();

            var managedIdentityCredential = new DefaultAzureCredential();

            var blobClientOptions = GetBlobClientOptions(_geoRedundantServiceUrl);

            var blobRequestConditions = new BlobRequestConditions() {  };

            var blobServiceClient = new BlobServiceClient(_primaryServiceUrl, managedIdentityCredential, blobClientOptions);

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var blobClient = containerClient.GetBlobClient(blobName).WithVersion(blobVersion);

            try
            {
                var result = await blobClient.DownloadStreamingAsync(conditions: blobRequestConditions, cancellationToken: cancellationToken);
                
                using var response = result.GetRawResponse();

                if (!IsSuccessStatusCode(response.Status))
                {
                    _logger?.LogDebug($"Unable to download file from blob storage.  {response.ClientRequestId} - Reported '{ response.ReasonPhrase }' with status code: '{ response.Status } { Enum.Parse(typeof(HttpStatusCode), Convert.ToString(response.Status, CultureInfo.InvariantCulture)) }'");

                    throw new IrretrievableFileException($"{response.ClientRequestId}: Unable to download file from storage.  Please consult log files for more information");
                }

                var details = result.Value.Details;

                // TODO - Seems odd I can't supply the expected hash (Content-MD5) in the request for blob storage to validate before
                //        starting the download (unless properties come back to us before stream is opened) so would be good to confirm
                //        things are working as we need with minimal overhead (given almost always likely to match)
                //        Need to test this works with larger files that might be chunked

                var blobContentHash = Convert.ToBase64String(details.BlobContentHash ?? details.ContentHash);

                if (0 != string.CompareOrdinal(blobContentHash, contentHash)) throw new IrretrievableFileException($"{response.ClientRequestId}: Unable to share the file with the user as the content hash stored during upload does not match that of the downloaded file - '{blobName}' + '{blobVersion}'");

                await result.Value.Content.CopyToAsync(streamToWriteTo, cancellationToken);

                return details;
            }
            catch (AuthenticationFailedException ex)
            {
                _logger?.LogError(ex, "Unable to authenticate with the Azure Blob Storage service using the default credentials");
                
                throw;
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger?.LogError(ex, $"Unable to access the storage endpoint as the download request failed: '{ ex.Status } { Enum.Parse(typeof(HttpStatusCode), Convert.ToString(ex.Status, CultureInfo.InvariantCulture)) }'");

                throw;
            }
        }

        async Task<Uri> IAzureBlobStoreClient.GenerateEphemeralDownloadLink(string containerName, string blobName, string blobVersion, string publicFacingBlobName, CancellationToken cancellationToken)
        {   
            // We will secure the link by creating a user delegate sas token signed by the managed identity of this application, thus
            // only the intersection of allowed permissions are applicable.   In this case, we only want to assign the read 
            // permission to the token, and for such access to be limited to a set period of time after which the token will expire
            //
            // If running local, note that the azure credentials resolved are those you are logged in as (for example the Visual Studio
            // azure account)

            if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentNullException(nameof(containerName));
            if (string.IsNullOrWhiteSpace(blobName)) throw new ArgumentNullException(nameof(blobName));
            if (string.IsNullOrWhiteSpace(blobVersion)) throw new ArgumentNullException(nameof(blobVersion));
            if (string.IsNullOrWhiteSpace(publicFacingBlobName)) throw new ArgumentNullException(nameof(publicFacingBlobName));

            cancellationToken.ThrowIfCancellationRequested();

            var managedIdentityCredential = new DefaultAzureCredential();

            var blobClientOptions = GetBlobClientOptions(_geoRedundantServiceUrl);

            var blobServiceClient = new BlobServiceClient(_primaryServiceUrl, managedIdentityCredential, blobClientOptions);

            var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var blobClient = blobContainerClient.GetBlobClient(blobName).WithVersion(blobVersion);

            var tokenStartsOn = _clock.UtcNow;

            var tokenExpiresOn = tokenStartsOn.AddMinutes(TOKEN_SAS_TIMEOUT_IN_MINUTES);

            var fileInfo = new FileInfo(publicFacingBlobName);

            var setContentDisposition = !string.IsNullOrWhiteSpace(fileInfo.Extension);

            var userDelegationKey = await _memoryCache.GetOrCreateAsync(
                $"{nameof(AzureBlobStoreClient)}:UserDelegationKey",
                async cacheEntry => 
                {
                    cacheEntry.Priority = CacheItemPriority.High;
                    cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);

                    try
                    {
                        var azureResponse = await blobServiceClient.GetUserDelegationKeyAsync(tokenStartsOn, tokenExpiresOn, cancellationToken);

                        return azureResponse.Value;
                    }
                    catch (Azure.RequestFailedException ex)
                    {
                        _logger?.LogError(ex, $"Unable to access the storage endpoint to generate a user delegation key: '{ ex.Status } { Enum.Parse(typeof(HttpStatusCode), Convert.ToString(ex.Status, CultureInfo.InvariantCulture)) }'");

                        throw;
                    }
                });

            var readOnlyPermission = BlobSasPermissions.Read;

            var blobSasBuilder = new BlobSasBuilder(readOnlyPermission, tokenExpiresOn)
            {
                BlobContainerName = blobContainerClient.Name,
                BlobName = blobClient.Name,
                BlobVersionId = blobVersion,
                Resource = "b",
                StartsOn = tokenStartsOn,
                ExpiresOn = tokenExpiresOn,
                Protocol = SasProtocol.Https,
                ContentDisposition = setContentDisposition ? $"attachment; filename*=UTF-8''{Uri.EscapeDataString(publicFacingBlobName)}" : default
                //PreauthorizedAgentObjectId = set this if we use AAD to authenticate our users, 
            };

            var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
            {
                Sas = blobSasBuilder.ToSasQueryParameters(userDelegationKey, blobServiceClient.AccountName)
            };

            var uri = blobUriBuilder.ToUri();

            return uri;
        }
    }
}
