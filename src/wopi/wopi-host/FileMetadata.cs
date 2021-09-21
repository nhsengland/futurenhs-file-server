using System;

namespace FutureNHS.WOPIHost
{
    public enum FileStatus: ushort
    {
        Uploading = 1, 
        Uploaded = 2,
        Failed = 3,
        Verified = 4,
    }

    public sealed class FileMetadata
    {
        internal static FileMetadata EMPTY = new FileMetadata();

        private FileMetadata() { }

        public FileMetadata(string title, string description, string groupName, string version, string owner, string name, string extension, ulong sizeInBytes, string blobName, DateTimeOffset lastWriteTime, string contentHash, FileStatus fileStatus)
        {
            if (string.IsNullOrWhiteSpace(title))       throw new ArgumentNullException(nameof(title));
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentNullException(nameof(description));
            if (string.IsNullOrWhiteSpace(groupName))   throw new ArgumentNullException(nameof(groupName));
            if (string.IsNullOrWhiteSpace(version))     throw new ArgumentNullException(nameof(version));
            if (string.IsNullOrWhiteSpace(owner))       throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(name))        throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(extension))   throw new ArgumentNullException(nameof(extension));
            if (string.IsNullOrWhiteSpace(blobName))    throw new ArgumentNullException(nameof(blobName));
            if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentNullException(nameof(contentHash));

            if (2 > extension.Length)                   throw new ArgumentOutOfRangeException(nameof(extension), "The file extension needs to be at least 2 characters long (including the period character)");
            if (!extension.StartsWith('.'))             throw new ArgumentOutOfRangeException(nameof(extension), "The file extension needs to start with a period character");
            if (0 >= sizeInBytes)                       throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "The file size needs to be greater than 0 bytes");

            // TODO - Might make sense to check the last write time isn't some crazy past of future date (clock skew)?

            Title = title;
            Description = description;
            Version = version;
            Owner = owner;
            Name = name;
            Extension = extension;
            BlobName = blobName;
            SizeInBytes = sizeInBytes;
            LastWriteTime = lastWriteTime;
            ContentHash = contentHash;
            FileStatus = fileStatus;
            GroupName = groupName;
        }

        public bool IsEmpty => ReferenceEquals(this, EMPTY);

        public string? Title { get; }
        public string? Description { get; }

        public string? GroupName { get; }

        public string? Name { get; }
        public string? Version { get; }
        public string? Extension { get; }

        public string? BlobName { get; }
        public string? ContentHash { get; }

        public string? Owner { get; } 

        public ulong? SizeInBytes { get; }
        public FileStatus? FileStatus { get; }
        public DateTimeOffset? LastWriteTime { get; } 
    }
}
