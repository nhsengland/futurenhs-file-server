using FutureNHS.WOPIHost.Azure;
using FutureNHS.WOPIHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost
{
    public interface IFileMetadataProvider
    {

        /// <summary>
        /// Tasked with retrieving the extended metadata for a specific file version
        /// </summary>
        /// <param name="file">The details of the file and version for which the extended metadata is being requested</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The requested metadata in the success case</returns>
        Task<FileMetadata> GetForFileAsync(File file, CancellationToken cancellationToken);
    }

    public sealed class FileMetadataProvider : IFileMetadataProvider
    {
        private readonly IAzureSqlClient _azureSqlClient;
        private readonly ILogger<FileMetadataProvider>? _logger;

        public FileMetadataProvider(IAzureSqlClient azureSqlClient, ILogger<FileMetadataProvider>? logger)
        {
            _logger = logger;

            _azureSqlClient = azureSqlClient ?? throw new ArgumentNullException(nameof(azureSqlClient));
        }

        async Task<FileMetadata> IFileMetadataProvider.GetForFileAsync(File file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.IsEmpty) throw new ArgumentNullException(nameof(file));

            Debug.Assert(!string.IsNullOrWhiteSpace(file.Name));

            var sb = new StringBuilder();

            sb.AppendLine($"SELECT   [{nameof(FileMetadata.Title)}]            = a.[Title]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Description)}]      = a.[Description]");
            sb.AppendLine($"       , [{nameof(FileMetadata.GroupName)}]        = d.[Name]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Name)}]             = a.[FileName]");
            sb.AppendLine($"       , [{nameof(FileMetadata.Version)}]          = @FileVersion");            
            sb.AppendLine($"       , [{nameof(FileMetadata.SizeInBytes)}]      = a.[FileSizeBytes]"); 
            sb.AppendLine($"       , [{nameof(FileMetadata.Extension)}]        = a.[FileExtension]");
            sb.AppendLine($"       , [{nameof(FileMetadata.BlobName)}]         = a.[BlobName]");     
            sb.AppendLine($"       , [{nameof(FileMetadata.ContentHash)}]      = CONVERT(NVARCHAR, a.[BlobHash], 2)"); 
            sb.AppendLine($"       , [{nameof(FileMetadata.LastWriteTime)}]    = CONVERT(DATETIMEOFFSET, ISNULL(a.[ModifiedAtUtc], a.[CreatedAtUtc]))"); // TODO - DB data type needs changing to datetimeoffset, or datetime2 with renamed to suffix UTC so we know what it contains
            sb.AppendLine($"       , [{nameof(FileMetadata.FileStatus)}]       = a.[FileStatus]"); 
            sb.AppendLine($"       , [{nameof(FileMetadata.Owner)}]            = b.[UserName]");
            sb.AppendLine($"FROM   dbo.[File]           a");
            sb.AppendLine($"JOIN   dbo.[MembershipUser] b ON b.[Id] = a.[CreatedBy]");
            sb.AppendLine($"JOIN   dbo.[Folder]         c ON c.[Id] = a.[ParentFolder]");
            sb.AppendLine($"JOIN   dbo.[Group]          d ON d.[Id] = c.[ParentGroup]");
            sb.AppendLine($"WHERE  a.[Id]               = @Id");
            //sb.AppendLine($"AND    (a.[FileVersion] = @FileVersion OR ...)");

            // TODO - If the file version parameter is null then we need to locate the current version of the file and return the metadata for it
            //        As versioning hasn't yet been implemented, we will need to return to this method and wire it up when we know how

            var parameters = new { Id = file.Name, FileVersion = file.Version };

            var fileMetadata = await _azureSqlClient.GetRecord<FileMetadata>(sb.ToString(), parameters, cancellationToken);

            return fileMetadata;
        }
    }
}
