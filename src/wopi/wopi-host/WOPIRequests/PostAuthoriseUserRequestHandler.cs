using FutureNHS.WOPIHost.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost.WOPIRequests
{
    internal sealed class PostAuthoriseUserRequestHandler
        : WopiRequestHandler
    {
        public enum FileAccessPermissions
        {
            View = 0,
            Edit = 1
        }

        private PostAuthoriseUserRequestHandler(string userAuthToken, FileAccessPermissions permission, File file)
            : base(userAuthToken, false, false)
        {
            if (file.IsEmpty) throw new ArgumentException("Cannot be EMPTY", nameof(file));

            if (!Enum.IsDefined(typeof(FileAccessPermissions), permission))
            {
                permission = FileAccessPermissions.View;
            }

            File = file;
            Permission = permission;
        }

        public FileAccessPermissions Permission { get; }
        public File File { get; }

        public static PostAuthoriseUserRequestHandler With(string userAuthToken, FileAccessPermissions permission, File file) => new (userAuthToken, permission, file);

        protected override async Task HandleAsyncImpl(HttpContext httpContext, CancellationToken cancellationToken)
        {
            // Try to get the discovery document from the WOPI client server (Collabora).  This tells us what file types are supported, and the
            // public endpoint that it hosts for it (that the browser needs to post back to in order to get said file)

            var wopiDiscoveryDocumentFactory = httpContext.RequestServices.GetRequiredService<IWopiDiscoveryDocumentFactory>();

            var wopiDiscoveryDocument = await wopiDiscoveryDocumentFactory.CreateDocumentAsync(cancellationToken);

            if (wopiDiscoveryDocument.IsEmpty) throw new ApplicationException("Unable to obtain the WOPI Discovery Document - most likely cause is the remote WOPI client is either unavailable or returned a non-success status code");

            Debug.Assert(!string.IsNullOrWhiteSpace(File.Id));

            var fileMetadataProvider = httpContext.RequestServices.GetRequiredService<IFileMetadataProvider>();

            var fileMetadata = await fileMetadataProvider.GetForFileAsync(File, cancellationToken);

            var fileExtension = fileMetadata.Extension;

            if (string.IsNullOrWhiteSpace(fileExtension)) throw new ApplicationException($"The file extension for the file with id '{File.Id}' is not stored in it's metadata");

            var wopiConfiguration = httpContext.RequestServices.GetRequiredService<IOptionsSnapshot<WopiConfiguration>>().Value;

            var hostFilesUrl = wopiConfiguration.HostFilesUrl;

            if (string.IsNullOrWhiteSpace(hostFilesUrl)) throw new ApplicationException("Unable to determine the HostFilesUrl from the application configuration.  Entry is null.");
            if (!Uri.IsWellFormedUriString(hostFilesUrl, UriKind.Absolute)) throw new ApplicationException($"Unable to determine the HostFilesUrl from the application configuration.  The entry is not a well formed absolute URL '{hostFilesUrl}'.");

            var fileAction = Permission == FileAccessPermissions.View ? "view" : "edit"; // edit | view | etc (see comments in discoveryDoc.GetEndpointForAsync)

            var wopiHostFileEndpointUrl = new Uri(System.IO.Path.Combine(hostFilesUrl, File.Id), UriKind.Absolute);  // TODO - Might be better suited to be a URL template with a named placeholder in case we ever need to support query parameters?

            var wopiClientEndpointForFileExtension = wopiDiscoveryDocument.GetEndpointForFileExtension(fileExtension, fileAction, wopiHostFileEndpointUrl);

            if (wopiClientEndpointForFileExtension is null || !wopiClientEndpointForFileExtension.IsAbsoluteUri) throw new ApplicationException($"The WOPI Client's endpoint for the requested file extension '{fileExtension}' could not be determined.  Ensure the file type is supported");

            var responseBody = new Dictionary<string, string>(2);

            Debug.Assert(!string.IsNullOrWhiteSpace(AccessToken));

            responseBody["accessToken"] = AccessToken;
            responseBody["wopiClientUrlForFile"] = wopiClientEndpointForFileExtension.AbsoluteUri;

            await httpContext.Response.WriteAsJsonAsync(responseBody, cancellationToken);
        }

        internal override bool IsAccessTokenValid() => true;
    }
}
