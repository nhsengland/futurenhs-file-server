using System;

namespace FutureNHS.WOPIHost
{
    public sealed class FileWriteDetails
    {
        public static FileWriteDetails EMPTY = new FileWriteDetails();

        private FileWriteDetails() { }

        public FileWriteDetails(string version, string contentType, byte[] contentHash, ulong contentLength, string? contentEncoding, string? contentLanguage, DateTimeOffset? lastAccessed, DateTimeOffset lastModified, FileMetadata fileMetadata)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentNullException(nameof(version));
            if (string.IsNullOrWhiteSpace(contentType)) throw new ArgumentNullException(nameof(contentType));
        
            if (0 > contentLength) throw new ArgumentOutOfRangeException(nameof(contentLength), "Must be greater than zero");

            Version = version;
            ContentLength = contentLength;
            ContentType = contentType;
            ContentEncoding = contentEncoding;
            ContentLanguage = contentLanguage;
            LastAccessed = lastAccessed;
            LastModified = lastModified;

            ContentHash = Convert.ToBase64String(contentHash);

            FileMetadata = fileMetadata ?? throw new ArgumentNullException(nameof(fileMetadata));
        }

        public bool IsEmpty => ReferenceEquals(this, EMPTY);

        public FileMetadata? FileMetadata { get; }

        public string? Version { get; }
        public string? ContentType { get; }
        public string? ContentHash { get; }
        public ulong? ContentLength { get; }
        public string? ContentEncoding { get; }
        public string? ContentLanguage { get; }
        public DateTimeOffset? LastAccessed { get; }
        public DateTimeOffset LastModified { get; }
    }
}
