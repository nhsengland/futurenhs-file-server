using FutureNHS.WOPIHost.Handlers;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests.Stubs
{
    internal sealed class WopiRequestStub : WopiRequest
    {
        private readonly Func<HttpContext, CancellationToken, Task> _handleAsyncImpl;
        private readonly Func<bool> _isUnableToValidateAccessToken;

        internal WopiRequestStub(Func<HttpContext, CancellationToken, Task> handleAsyncImpl, Func<bool> isUnableToValidateAccessToken = default)
            : base(accessToken: "access-token", isWriteAccessRequired: false, demandsProof: true)
        {
            _handleAsyncImpl = handleAsyncImpl;
            _isUnableToValidateAccessToken = isUnableToValidateAccessToken;
        }

        protected override Task HandleAsyncImpl(HttpContext context, CancellationToken cancellationToken)
        {
            return _handleAsyncImpl?.Invoke(context, cancellationToken);
        }

        internal override bool IsUnableToValidateAccessToken()
        {
            return _isUnableToValidateAccessToken?.Invoke() ?? base.IsUnableToValidateAccessToken();
        }
    }
}
