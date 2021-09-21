using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.PlatformHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost
{
    public interface IFileRepository
    {
        /// <summary>
        /// Tasked with retrieving a file located in storage and writing it into <paramref name="streamToWriteTo"/>
        /// </summary>
        /// <param name="fileMetadata">The metadata pertinent to the file we are going to try and write to the stream</param>
        /// <param name="streamToWriteTo">The stream to which the content of the file will be written in the success case/></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<FileWriteDetails> WriteToStreamAsync(FileMetadata fileMetadata, Stream streamToWriteTo, CancellationToken cancellationToken);

        /// <summary>
        /// Tasked with retrieving the extended metadata for a specific file version
        /// </summary>
        /// <param name="file">The details of the file and version for which the extended metadata is being requested</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The requested metadata in the success case</returns>
        Task<FileMetadata> GetMetadataAsync(File file, CancellationToken cancellationToken);

        /// <summary>
        /// Tasked with retrieving an ephemeral endpoint from which the contents of the file can be directly downloaded without the 
        /// need for it to be proxied through our file server.  The returned url will be formed such that traffic from outside of the 
        /// virtual network will be routed through our application gateway
        /// </summary>
        /// <param name="fileMetadata">The metadata for the file to which the ephemeral endpoint will be explicitly tied</param>
        /// <param name="cancellationToken"></param>
        /// <returns>In the success case, the url of the endpoint else a null value</returns>
        Task<Uri> GeneratePublicEphemeralDownloadLink(FileMetadata file, CancellationToken cancellationToken);

        /// <summary>
        /// Tasked with retrieving an ephemeral endpoint from which the contents of the file can be directly downloaded without the 
        /// need for it to be proxied through our file server.  The returned url will be formed such that it can only be used by a 
        /// caller whom resides within the same virtual network as our storage
        /// </summary>
        /// <param name="fileMetadata">The metadata for the file to which the ephemeral endpoint will be explicitly tied</param>
        /// <param name="cancellationToken"></param>
        /// <returns>In the success case, the url of the endpoint else a null value</returns>
        Task<Uri> GeneratePrivateEphemeralDownloadLink(FileMetadata file, CancellationToken cancellationToken);
    }

    public sealed class FileRepository : IFileRepository
    {
        private readonly IAzureBlobStoreClient _azureBlobStoreClient;
        private readonly IAzureSqlClient _azureSqlClient;
        private readonly ILogger<FileRepository>? _logger;

        private readonly string _blobContainerName;

        public FileRepository(IAzureBlobStoreClient azureBlobStoreClient, IAzureSqlClient azureSqlClient, IOptionsSnapshot<AzurePlatformConfiguration> azurePlatformConfiguration, ILogger<FileRepository>? logger)
        {
            _logger = logger;

            _azureBlobStoreClient = azureBlobStoreClient ?? throw new ArgumentNullException(nameof(azureBlobStoreClient));
            _azureSqlClient = azureSqlClient             ?? throw new ArgumentNullException(nameof(azureSqlClient));

            if (azurePlatformConfiguration?.Value is null) throw new ArgumentNullException(nameof(azurePlatformConfiguration));

            var blobContainerName = azurePlatformConfiguration.Value.AzureBlobStorage?.ContainerName;

            if (string.IsNullOrWhiteSpace(blobContainerName)) throw new ApplicationException("The files blob container name is not set in the configuration");

            _blobContainerName = blobContainerName;
        }

        async Task<FileMetadata> IFileRepository.GetMetadataAsync(File file, CancellationToken cancellationToken)
        {
            if (file.IsEmpty) throw new ArgumentNullException(nameof(file));

            cancellationToken.ThrowIfCancellationRequested();

            var sb = new StringBuilder();

            sb.AppendLine($"SELECT   [{nameof(FileMetadata.Title)}]            = a.[Title]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Description)}]      = a.[Description]");
            sb.AppendLine($"       , [{nameof(FileMetadata.GroupName)}]        = d.[Name]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Name)}]             = a.[FileName]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Version)}]          = @FileVersion");            // TODO - Wire up when in database
            sb.AppendLine($"       , [{nameof(FileMetadata.SizeInBytes)}]      = a.[FileSize]");            // TODO - to be renamed in database
            sb.AppendLine($"       , [{nameof(FileMetadata.Extension)}]        = a.[FileExtension]");
            sb.AppendLine($"       , [{nameof(FileMetadata.BlobName)}]         = a.[FileUrl]");             // TODO - to be renamed in database
            sb.AppendLine($"       , [{nameof(FileMetadata.ContentHash)}]      = @FileContentHash");        // TODO - Wire up when in database
            sb.AppendLine($"       , [{nameof(FileMetadata.LastWriteTime)}]    = CONVERT(DATETIMEOFFSET, ISNULL(a.[ModifiedAtUtc], a.[CreatedAtUtc]))"); // TODO - DB data type needs changing to datetimeoffset, or datetime2 with renamed to suffix UTC so we know what it contains
            sb.AppendLine($"       , [{nameof(FileMetadata.FileStatus)}]       = a.[UploadStatus]");        // TODO - Earmarked to be renamed in DB to FileStatus
            sb.AppendLine($"       , [{nameof(FileMetadata.Owner)}]            = b.[UserName]");
            sb.AppendLine($"FROM   dbo.[File]           a");
            sb.AppendLine($"JOIN   dbo.[MembershipUser] b ON b.[Id] = a.[CreatedBy]");
            sb.AppendLine($"JOIN   dbo.[Folder]         c ON c.[Id] = a.[ParentFolder]");
            sb.AppendLine($"JOIN   dbo.[Group]          d ON d.[Id] = c.[ParentGroup]");
            sb.AppendLine($"WHERE  a.[Id]               = @Id");

            var parameters = new { Id = file.Name, FileVersion = file.Version, FileContentHash = "replace-with-hash-code-soon" };

            var fileMetadata = await _azureSqlClient.GetRecord<FileMetadata>(sb.ToString(), parameters, cancellationToken);

            return fileMetadata;
        }

        async Task<FileWriteDetails> IFileRepository.WriteToStreamAsync(FileMetadata fileMetadata, Stream streamToWriteTo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fileMetadata is null || fileMetadata.IsEmpty) throw new ArgumentNullException(nameof(fileMetadata));

            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.BlobName));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.Version));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.ContentHash));

            var downloadDetails = await _azureBlobStoreClient.FetchBlobAndWriteToStream(_blobContainerName, fileMetadata.BlobName, fileMetadata.Version, fileMetadata.ContentHash, streamToWriteTo, cancellationToken);

            return new FileWriteDetails(
                version: downloadDetails.VersionId,
                contentHash: downloadDetails.ContentHash,               // https://blogs.msdn.microsoft.com/windowsazurestorage/2011/02/17/windows-azure-blob-md5-overview/
                contentEncoding: downloadDetails.ContentEncoding,
                contentLanguage: downloadDetails.ContentLanguage,
                contentType: downloadDetails.ContentType,
                contentLength: 0 > downloadDetails.ContentLength ? 0 : (ulong)downloadDetails.ContentLength,
                lastAccessed: DateTimeOffset.MinValue == downloadDetails.LastAccessed ? default : downloadDetails.LastAccessed,
                lastModified: downloadDetails.LastModified,
                fileMetadata: fileMetadata
                );
        }

        async Task<Uri> IFileRepository.GeneratePublicEphemeralDownloadLink(FileMetadata fileMetadata, CancellationToken cancellationToken)
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
            
            var uriBuilder = new UriBuilder {
                Path = string.Concat("gateway/media", blobUri.LocalPath),
                Query = blobUri.Query
            };

            return uriBuilder.Uri;
        }

        async Task<Uri> IFileRepository.GeneratePrivateEphemeralDownloadLink(FileMetadata fileMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fileMetadata is null || fileMetadata.IsEmpty) throw new ArgumentNullException(nameof(fileMetadata));

            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.BlobName));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.Version));
            Debug.Assert(!string.IsNullOrWhiteSpace(fileMetadata.Name));

            var blobUri = await _azureBlobStoreClient.GenerateEphemeralDownloadLink(_blobContainerName, fileMetadata.BlobName, fileMetadata.Version, fileMetadata.Name, cancellationToken);

            // The uri we are returning directly accesses the blob storage account.  It will not be routed through our application 
            // gateway

            return blobUri;
        }
    }
}
