using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace FutureNHS.WOPIHost
{
    public interface IWopiRequestFactory
    {
        bool TryCreateRequest(HttpRequest request, out WopiRequest wopiRequest);
    }

    internal sealed class WopiRequestFactory
        : IWopiRequestFactory
    {
        private readonly Features _features;
        private readonly ILogger<WopiRequestFactory>? _logger;

        public WopiRequestFactory(IOptionsSnapshot<Features> features, ILogger<WopiRequestFactory>? logger = default)
        {
            _features = features?.Value ?? throw new ArgumentNullException(nameof(features.Value));

            _logger = logger;
        }

        private bool EmptyResponse(string traceInfo) { _logger?.LogTrace(traceInfo); return false; }

        bool IWopiRequestFactory.TryCreateRequest(HttpRequest httpRequest, out WopiRequest wopiRequest)
        {
            if (httpRequest is null) throw new ArgumentNullException(nameof(httpRequest));

            wopiRequest = WopiRequest.EMPTY;

            var path = httpRequest.Path;

            if (path.HasValue && path.StartsWithSegments("/wopi", StringComparison.OrdinalIgnoreCase))
            {
                var accessToken = httpRequest.Query["access_token"].FirstOrDefault();

                if (string.IsNullOrWhiteSpace(accessToken)) return EmptyResponse("The access token query parameter is either missing, or it has an empty value"); // TODO - Might be better to be more specific with a WopiRequest.MissingAccessToken response?

                if (path.StartsWithSegments("/wopi/files", StringComparison.OrdinalIgnoreCase))
                {
                    wopiRequest = IdentifyFileRequest(httpRequest, path, accessToken, _features);
                }
                else if (path.StartsWithSegments("/wopi/folders", StringComparison.OrdinalIgnoreCase))
                {
                    wopiRequest = IdentifyFolderRequest();
                }
                else return EmptyResponse($"Failed to identify WOPI request.  Endpoint '{path}' not supported");

                if (wopiRequest.IsUnableToValidateAccessToken())
                {
                    wopiRequest = WopiRequest.EMPTY;

                    return EmptyResponse($"The access token provided '{accessToken}' could not be identified as valid"); // TODO - Might be better to be more specific with a WopiRequest.InvalidAccessToken response?
                }
                else return true;  // Valid WOPI request that can be actioned
            }

            _logger?.LogTrace($"Determined the request does not related to WOPI: '{path.Value}'");

            return false;  // Not a WOPI request so nothing for us to do
        }

        private WopiRequest IdentifyFileRequest(HttpRequest httpRequest, PathString path, string accessToken, Features features)
        {
            var fileSegment = path.Value.Substring("/wopi/files/".Length)?.Trim();

            var method = httpRequest.Method;

            _logger?.LogTrace($"Extracted file segment from request path: '{fileSegment ?? "null"}' for method {method ?? "null"}");
            
            if (string.IsNullOrWhiteSpace(fileSegment)) return WopiRequest.EMPTY;

            // NB - Collabora have not implemented support for the X-WOPI-ItemVersion header and so the Version field set in the 
            //      CheckFileInfo response does not flow through to those endpoints where it is optional - eg GetFile.
            //      This unfortunately means we have to do some crazy workaround using the fileId, and thus use that to derive 
            //      the relevant metadata needed for us to operate correctly.  Hopefully this will prove to be just a temporary
            //      workaround

            if (fileSegment.EndsWith("/contents"))
            {
                var fileId = fileSegment.Substring(0, fileSegment.Length - "/contents".Length)?.Trim() ?? "null";

                _logger?.LogTrace($"Identified 'contents' request.  File Id extracted from url is: '{fileId}'");

                if (string.IsNullOrWhiteSpace(fileId)) return WopiRequest.EMPTY;

                var fileVersion = httpRequest.Headers["X-WOPI-ItemVersion"].FirstOrDefault();

                var file = File.FromId(fileId, fileVersion);

                if (0 == string.Compare("GET", method, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogTrace($"Identified this to be a Get File request");

                    var isEphemeralRedirect = bool.Parse(httpRequest.Query["ephemeral_redirect"].FirstOrDefault() ?? bool.FalseString);

                    if (isEphemeralRedirect) return RedirectToFileStoreRequest.With(file, accessToken);
                    
                    return GetFileWopiRequest.With(file, accessToken);
                }
                else if (0 == string.Compare("POST", method, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogTrace($"Identified this to be a Save File request");

                    return PostFileWopiRequest.With(file.Name, accessToken); 
                }
                else _logger?.LogTrace($"The request method '{method}' is not supported for path '{path.Value ?? "null"}'");
            }
            else
            {
                var fileId = fileSegment;

                _logger?.LogTrace($"File Id extracted from url is: '{fileId}'.  Attempting to identity request type");

                if (0 == string.Compare("GET", method, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogTrace($"Identified this is a Check File Info request");

                    var file = (File)fileId;

                    return CheckFileInfoWopiRequest.With(file, accessToken, features);
                }
                else _logger?.LogTrace($"The request method '{method}' is not supported for path '{path.Value ?? "null"}");
            }

            return WopiRequest.EMPTY;
        }

        private static WopiRequest IdentifyFolderRequest()
        {
            return WopiRequest.EMPTY;  // Not supported
        }
    }
}
