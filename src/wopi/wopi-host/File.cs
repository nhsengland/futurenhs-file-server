﻿using System;

namespace FutureNHS.WOPIHost
{
    public readonly struct File
        : IEquatable<File>
    {
        internal const int FILENAME_MAXIMUM_LENGTH = 100;
        internal const int FILENAME_MINIMUM_LENGTH = 4;

        private File(string fileName, string? fileVersion)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));

            fileName = fileName.Trim();
            fileVersion = fileVersion?.Trim();

            if (FILENAME_MAXIMUM_LENGTH < fileName.Length) throw new ArgumentOutOfRangeException(nameof(fileName), $"Maximum allowed filename length is {FILENAME_MAXIMUM_LENGTH} characters");
            if (FILENAME_MINIMUM_LENGTH > fileName.Length) throw new ArgumentOutOfRangeException(nameof(fileName), $"Minimum allowed filename length is {FILENAME_MINIMUM_LENGTH} characters");

            Name = fileName;
            Version = fileVersion;

            Id = string.IsNullOrWhiteSpace(fileVersion) ? fileName : string.Concat(fileName, '|', fileVersion);
        }

        public static File EMPTY { get; } = new File();

        public string? Id { get; }
        public string? Name { get; }
        public string? Version { get; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Version);

        public static File With(string fileName, string fileVersion)
        {
            return new File(fileName, fileVersion);
        }

        public static File FromId(string id, string? fileVersion = default)
        {
            // The version of the file may be provided in the id, or alternatively in the header of the request if 
            // the WOPI client fully supported the versioning protocol.   The optional parameter is the value taken 
            // from the header if it exists.

            if (string.IsNullOrWhiteSpace(id)) return EMPTY;

            var segments = id.Split('|', StringSplitOptions.RemoveEmptyEntries);

            var fileName = segments[0];

            var fileVersionFromId = 2 <= segments.Length ? segments[1] : default;

            if (string.IsNullOrWhiteSpace(fileVersion)) fileVersion = fileVersionFromId;

            if (string.IsNullOrWhiteSpace(fileName)) return EMPTY;

            if (fileVersionFromId is not null && fileVersion is not null && !fileVersion.Equals(fileVersionFromId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"The file version taken from the X-WOPI-ItemVersion header '{fileVersion}' differs from the version encoded in the file id '{fileVersionFromId}'");

            return new File(fileName.Trim(), fileVersion?.Trim());
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;

                hash = hash * 23 + (Name ?? string.Empty).GetHashCode();
                hash = hash * 23 + (Version ?? string.Empty).GetHashCode();

                return hash;
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;

            if (!typeof(File).IsAssignableFrom(obj.GetType())) return false;

            return Equals((File)obj);
        }

        public bool Equals(File other)
        {
            if (other.IsEmpty) return IsEmpty;

            if (0 != string.Compare(other.Name, Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (0 != string.CompareOrdinal(other.Version, Version)) return false;

            return true;
        }

        public bool Equals(File? other)
        {
            if (other is null) return IsEmpty;

            return Equals(other.Value);
        }

        public static bool operator ==(File left, File right) => left.Equals(right);
        public static bool operator !=(File left, File right) => !(left.Equals(right));

        public static implicit operator string?(File file) => file.IsEmpty ? default : file.Id;
        public static implicit operator File(string id) => FromId(id);

        public override string ToString()
        {
            return $"File Name = '{Name ?? "null"}', File Version = '{Version ?? "null"}'";
        }
    }
}
