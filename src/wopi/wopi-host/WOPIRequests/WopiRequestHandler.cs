using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost.WOPIRequests
{
    public abstract class WopiRequestHandler
    {
        internal static readonly WopiRequestHandler Empty = new EmptyWopiRequest();

        private readonly bool _isWriteAccessRequired = false;

        protected WopiRequestHandler() { }

        protected WopiRequestHandler(string accessToken, bool isWriteAccessRequired, bool demandsProof)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentNullException(nameof(accessToken));

            AccessToken = accessToken;
            DemandsProof = demandsProof;

            _isWriteAccessRequired = isWriteAccessRequired;
        }

        public string? AccessToken { get; }

        public bool? DemandsProof { get; }

        internal bool IsEmpty => ReferenceEquals(this, WopiRequestHandler.Empty);

        internal Task HandleAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            if (IsEmpty) throw new InvalidOperationException("An empty wopi request cannot handle an http context.  Check IsEmpty before invoking this method");

            cancellationToken.ThrowIfCancellationRequested();

            return HandleAsyncImpl(httpContext, cancellationToken);
        }

        /// <summary>
        /// Tasked with handling the specific request that the concrete class represents
        /// </summary>
        /// <param name="httpContext">The http context associated with the request</param>
        /// <param name="cancellationToken">A token used to signel when the unit of work can be terminate before completion</param>
        /// <returns></returns>
        protected abstract Task HandleAsyncImpl(HttpContext httpContext, CancellationToken cancellationToken);

        /// <summary>
        /// TODO - This is where we need to implement our own token validation logic
        /// At the moment we have a fake guid being passed around but this need to be implemented properly for production
        /// </summary>
        /// <returns></returns>
        internal virtual bool IsAccessTokenValid() => !string.IsNullOrWhiteSpace(AccessToken);
            
        /// <summary>
        /// Helper class used to capture a failure to identify/verify a potential WOPI request such that we don't have to 
        /// propagate nulls throughout the code.  Could equally be switched out for a Maybe type upon review
        /// </summary>
        private sealed class EmptyWopiRequest : WopiRequestHandler
        {
            internal EmptyWopiRequest() { }

            protected override Task HandleAsyncImpl(HttpContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
        }
    }
}
