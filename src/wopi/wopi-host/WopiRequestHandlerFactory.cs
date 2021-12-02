using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.WOPIRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Linq;

namespace FutureNHS.WOPIHost
{
    public interface IWopiRequestHandlerFactory
    {
        bool TryCreateRequestHandler(HttpRequest request, out WopiRequestHandler wopiRequestHandler);
    }

    internal sealed class WopiRequestHandlerFactory
        : IWopiRequestHandlerFactory
    {
        private const string WOPI_PATH_SEGMENT = "/wopi";
        private const string WOPI_FILES_PATH_SEGMENT = WOPI_PATH_SEGMENT + "/files";
        private const string WOPI_FOLDERS_PATH_SEGMENT = WOPI_PATH_SEGMENT + "/folders";

        private readonly Features _features;
        private readonly ILogger<WopiRequestHandlerFactory>? _logger;

        public WopiRequestHandlerFactory(IOptionsSnapshot<Features> features, ILogger<WopiRequestHandlerFactory>? logger = default)
        {
            _features = features?.Value ?? throw new ArgumentNullException(nameof(features));

            _logger = logger;
        }

#pragma warning disable CA2254 // Template should be a static expression
        private bool InvalidWopiRequest(string message, params object[] args) { _logger?.LogTrace(message, args); return false; }
#pragma warning restore CA2254 // Template should be a static expression

        bool IWopiRequestHandlerFactory.TryCreateRequestHandler(HttpRequest httpRequest, out WopiRequestHandler wopiRequestHandler)
        {
            const bool THIS_IS_A_VALID_WOPI_FILE_REQUEST = true;
            const bool THIS_IS_NOT_A_WOPI_FILE_REQUEST = false;

            if (httpRequest is null) throw new ArgumentNullException(nameof(httpRequest));

            wopiRequestHandler = WopiRequestHandler.Empty;

            var requestPath = httpRequest.Path;

            if (requestPath.HasValue && requestPath.StartsWithSegments(WOPI_PATH_SEGMENT, StringComparison.OrdinalIgnoreCase))
            {
                var accessToken = httpRequest.Query["access_token"].FirstOrDefault();

                if (requestPath.StartsWithSegments(WOPI_FILES_PATH_SEGMENT, StringComparison.OrdinalIgnoreCase))
                {
                    wopiRequestHandler = IdentifyFileRequest(httpRequest, accessToken);
                }
                else if (requestPath.StartsWithSegments(WOPI_FOLDERS_PATH_SEGMENT, StringComparison.OrdinalIgnoreCase))
                {
                    wopiRequestHandler = IdentifyFolderRequest();
                }
                else return InvalidWopiRequest("Failed to identify WOPI request.  Endpoint '{RequestPath}' not supported", requestPath);

                if (wopiRequestHandler.IsAccessTokenValid()) return THIS_IS_A_VALID_WOPI_FILE_REQUEST;

                wopiRequestHandler = WopiRequestHandler.Empty;

                return InvalidWopiRequest("The access token provided '{AccessToken}' is invalid.   The WOPI request cannot be handled.", accessToken ?? "null");
            }

            _logger?.LogTrace("Determined the request does not relate to WOPI: '{RequestPath}'", requestPath.Value);

            return THIS_IS_NOT_A_WOPI_FILE_REQUEST;  
        }

        private WopiRequestHandler IdentifyFileRequest(HttpRequest httpRequest, string? accessToken)
        {
            var requestMethod = httpRequest.Method;

            var requestPath = httpRequest.Path.Value;

            if (string.IsNullOrWhiteSpace(requestPath)) return WopiRequestHandler.Empty;

 
            var fileSegmentOfRequestPath = requestPath?[WOPI_FILES_PATH_SEGMENT.Length..]?.Trim();

            _logger?.LogTrace("Extracted file segment from request path: '{PathSegment}' for method '{RequestMethod}'", fileSegmentOfRequestPath ?? "null", requestMethod);
            
            if (string.IsNullOrWhiteSpace(fileSegmentOfRequestPath)) return WopiRequestHandler.Empty;

            Debug.Assert(fileSegmentOfRequestPath.StartsWith('/'));

            fileSegmentOfRequestPath = fileSegmentOfRequestPath[1..];

            Debug.Assert(!string.IsNullOrWhiteSpace(requestPath));

            if (fileSegmentOfRequestPath.EndsWith("/contents"))
            {
                if (string.IsNullOrWhiteSpace(accessToken)) return WopiRequestHandler.Empty;

                return ConfigureFileContentRequestHandler(httpRequest, requestMethod, requestPath, fileSegmentOfRequestPath, accessToken);
            }
            else if (fileSegmentOfRequestPath.EndsWith("/authorise-user"))
            {
                return ConfigureAuthoriseUserRequestHandler(requestPath, requestMethod, fileSegmentOfRequestPath, httpRequest.Query);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(accessToken)) return WopiRequestHandler.Empty;

                return ConfigureCheckFileInfoRequestHandler(requestPath, requestMethod, fileSegmentOfRequestPath, accessToken);
            }
        }

        private WopiRequestHandler ConfigureFileContentRequestHandler(HttpRequest httpRequest, string requestMethod, string requestPath, string fileSegmentOfRequestPath, string accessToken)
        {
            var fileId = fileSegmentOfRequestPath.Substring(0, fileSegmentOfRequestPath.Length - "/contents".Length)?.Trim() ?? "null";

            _logger?.LogTrace("Identified 'contents' request.  File Id extracted from url is: '{FileId}'", fileId);

            if (string.IsNullOrWhiteSpace(fileId)) return WopiRequestHandler.Empty;

            // NB - Collabora have not implemented support for the X-WOPI-ItemVersion header and so the Version field set in the 
            //      CheckFileInfo response does not flow through to those endpoints where it is optional - eg GetFile.
            //      This unfortunately means we have to do some crazy workaround using the fileId, and thus use that to derive 
            //      the relevant metadata needed for us to operate correctly.  Hopefully this will prove to be just a temporary
            //      workaround

            var fileVersion = httpRequest.Headers["X-WOPI-ItemVersion"].FirstOrDefault();

            var file = File.FromId(fileId, fileVersion);

            if (0 == string.Compare("GET", requestMethod, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogTrace("Identified this to be a WOPI 'Get File' request");

                var isEphemeralRedirect = bool.Parse(httpRequest.Query["ephemeral_redirect"].FirstOrDefault() ?? bool.FalseString);

                if (isEphemeralRedirect) return RedirectToFileStoreRequestHandler.With(file, accessToken);

                return GetFileWopiRequestHandler.With(file, accessToken);
            }
            else if (0 == string.Compare("POST", requestMethod, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogTrace("Identified this to be a WOPI 'Save File' request");

                Debug.Assert(!string.IsNullOrWhiteSpace(file.Name));

                return PostFileWopiRequestHandler.With(file.Name, accessToken);
            }
            else _logger?.LogTrace("The request method '{RequestMethod}' is not supported for path '{RequestPath}'", requestMethod, requestPath ?? "null");

            return WopiRequestHandler.Empty;
        }

        private  WopiRequestHandler ConfigureAuthoriseUserRequestHandler(string requestPath, string requestMethod, string fileSegmentOfRequestPath, IQueryCollection query)
        {
            var fileId = fileSegmentOfRequestPath.Replace("/authorise-user", string.Empty);

            _logger?.LogTrace("File Id extracted from url is: '{FileId}'.  Attempting to identity permission type", fileId ?? "null");

            if (string.IsNullOrWhiteSpace(fileId)) return WopiRequestHandler.Empty;

            var permission =
                0 == string.Compare(query["permission"], "edit", StringComparison.OrdinalIgnoreCase)
                ? PostAuthoriseUserRequestHandler.FileAccessPermissions.Edit
                : PostAuthoriseUserRequestHandler.FileAccessPermissions.View;

            _logger?.LogTrace("Permission type extracted from url query is: '{Permission}'.  Attempting to identity request type", permission);

            if (0 == string.Compare("POST", requestMethod, StringComparison.OrdinalIgnoreCase))
            {
                // This isn't actually part of the WOPI specification; more a customisation we're making to get the endpoint for the WOPI host
                // from where the file can be viewed/edited, and an access_token it can pass back to us when requesting the same

                // TODO - Need to send the auth cookie across to MVCForum to authenticate (when it can do so).  It will return the user details that we can then 
                //        consider when we add authorisation to file requests.   In the meantime, we'll just send back a random token

                _logger?.LogTrace("Identified this to be an 'Authorise User' request");
                
                var userAuthToken = Guid.NewGuid().ToString().ToLowerInvariant().Replace("-", string.Empty);

                return PostAuthoriseUserRequestHandler.With(userAuthToken, permission, fileId);
            }
            else _logger?.LogWarning("The request method '{RequestMethod}' is not supported for path '{RequestPath}", requestMethod, requestPath);

            return WopiRequestHandler.Empty;
        }

        private WopiRequestHandler ConfigureCheckFileInfoRequestHandler(string requestPath, string requestMethod, string fileId, string accessToken)
        {
            _logger?.LogTrace("File Id extracted from url is: '{FileId}'.  Attempting to identity request type", fileId);

            if (0 == string.Compare("GET", requestMethod, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogTrace("Identified this is a WOPI 'Check File Info' request");

                var file = (File)fileId;

                return CheckFileInfoWopiRequestHandler.With(file, accessToken, _features);
            }
            else _logger?.LogWarning("The request method '{RequestMethod}' is not supported for path '{RequestPath}", requestMethod, requestPath ?? "null");

            return WopiRequestHandler.Empty;
        }

        private static WopiRequestHandler IdentifyFolderRequest()
        {
            return WopiRequestHandler.Empty;  // Not supported
        }
    }
}
