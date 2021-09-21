using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace FutureNHS.WOPIHost
{
    /// <summary>
    /// Defines a service that is tasked with checking a given proof has indeed been signed by a WOPI Client whom this
    /// application is configured to trust, and thus requests issued by it are considered non-repudiable
    /// </summary>
    /// <remarks>https://wopi.readthedocs.io/en/latest/scenarios/proofkeys.html</remarks>
    public interface IWopiCryptoProofChecker
    {
        (bool isInvalid, bool refetchProofKeys) IsProofInvalid(HttpRequest httpRequest, IWopiProofKeysProvider wopiProofKeysProvider);
    }

    public interface IWopiProofKeysProvider
    {
        bool IsEmpty { get; }

        string? PublicKeyCspBlob { get; }
        string? OldPublicKeyCspBlob { get; }
    }

    public sealed class WopiCryptoProofChecker : IWopiCryptoProofChecker
    {
        private readonly ILogger<WopiCryptoProofChecker> _logger;

        public WopiCryptoProofChecker(ILogger<WopiCryptoProofChecker> logger)
        {
            _logger = logger;
        }

        (bool isInvalid, bool refetchProofKeys) IWopiCryptoProofChecker.IsProofInvalid(HttpRequest httpRequest, IWopiProofKeysProvider wopiProofKeysProvider)
        {
            if (httpRequest is null) throw new ArgumentNullException(nameof(httpRequest));
            if (wopiProofKeysProvider is null) throw new ArgumentNullException(nameof(wopiProofKeysProvider));
            if (wopiProofKeysProvider.IsEmpty) throw new ArgumentOutOfRangeException(nameof(wopiProofKeysProvider), "The prrof keys provider cannot be EMPTY.  Please check it's state before calling this method");

            const bool PROOF_IS_INVALID = true;
            const bool PROOF_IS_VALID = false;

            var encodedUrl = new Uri(httpRequest.GetEncodedUrl(), UriKind.Absolute);

            var accessToken = HttpUtility.ParseQueryString(encodedUrl.Query)["access_token"];

            var encodedAccessToken = HttpUtility.UrlEncode(accessToken);

            var encodedRequestUrl = httpRequest.GetEncodedUrl();

            var wopiHostUrl = encodedRequestUrl.ToUpperInvariant();

            var wopiTimestamp = httpRequest.Headers["X-WOPI-Timestamp"].SingleOrDefault();

            if (string.IsNullOrWhiteSpace(wopiTimestamp)) throw new InvalidOperationException("The X-WOPI-Timestamp header is missing from the request and the caller is required to present proof");

            var timestamp = Convert.ToInt64(wopiTimestamp.Trim());

            var accessTokenBytes = Encoding.UTF8.GetBytes(encodedAccessToken);
            var wopiHostUrlBytes = Encoding.UTF8.GetBytes(wopiHostUrl);
            var timestampBytes = BitConverter.GetBytes(timestamp).Reverse().ToArray();

            var proof = new List<byte>(4 + accessTokenBytes.Length + 4 + wopiHostUrlBytes.Length + 4 + timestampBytes.Length);

            proof.AddRange(BitConverter.GetBytes(accessTokenBytes.Length).Reverse());
            proof.AddRange(accessTokenBytes);
            proof.AddRange(BitConverter.GetBytes(wopiHostUrlBytes.Length).Reverse());
            proof.AddRange(wopiHostUrlBytes);
            proof.AddRange(BitConverter.GetBytes(timestampBytes.Length).Reverse());
            proof.AddRange(timestampBytes);

            var expectedProof = proof.ToArray();

            var givenProof = httpRequest.Headers["X-WOPI-Proof"].Single().Trim();
            var oldGivenProof = httpRequest.Headers["X-WOPI-ProofOld"].Single().Trim();

            var publicKeyCspBlob = wopiProofKeysProvider.PublicKeyCspBlob;
            var oldPublicKeyCspBlob = wopiProofKeysProvider.OldPublicKeyCspBlob;
#if DEBUG
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: request_url = {0}", encodedRequestUrl);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: access_token = {0}", encodedAccessToken);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: proof-key.value = {0}", publicKeyCspBlob);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: proof-key.oldvalue = {0}", oldPublicKeyCspBlob);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: X-WOPI-Timestamp = {0}", timestamp);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: X-WOPI-Proof = {0}", givenProof);
            _logger?.LogDebug($"{nameof(WopiCryptoProofChecker)}-{nameof(IWopiCryptoProofChecker.IsProofInvalid)}: X-WOPI-ProofOld = {0}", oldGivenProof);
#endif
            // Is the proof verifiable using either our current key or the old one?  If not, maybe there is a new key that we 
            // do not know about, thus we might be able to verify using the old proof with our current key (ie our current key is old
            // but we are still working with a now outdated discovery document which we need to refresh).

            if (IsProven(expectedProof, givenProof, publicKeyCspBlob)) return (PROOF_IS_VALID, refetchProofKeys: false);                              // discovery doc is the latest
            if (IsProven(expectedProof, oldGivenProof, publicKeyCspBlob)) return (PROOF_IS_VALID, refetchProofKeys: true);     // discovery doc needs to be refreshed

            // Next scenario is one where our discovery document is up to date, but the proof was generated using an old key and if 
            // that doesn't work then using the old key to sign the old proof but having the new key fail to validate the new proof
            // smacks of dodgy shenanigans so I guess we'll just let that one fail

            if (IsProven(expectedProof, givenProof, oldPublicKeyCspBlob)) return (PROOF_IS_VALID, refetchProofKeys: false);

            // There is a scenario that is impossible for us to distinguish from a potential attack, and that is the one where 
            // the WOPI client has rotated the keys mutliple times since we last refreshed the discovery document. Safest thing for 
            // us to do is to refetch the document whenever something fails validation and mitigate the DDoS vector this opens up 
            // at the infrastructure level.

            return (PROOF_IS_INVALID, refetchProofKeys: true);
        }

        /// <summary>
        /// Tasked with verifying that the presented proof does indeed match that which we would have expected the 
        /// trusted WOPI client (from which we pulled the Discovery Document we represent) to have produced for the 
        /// specific request that has been made to us (the WOPI Host).  This is the approach taken to ensure the request
        /// validity is non-repudiable
        /// </summary>
        /// <param name="expectedProof">The proof we would have expected the trusted client to offer</param>
        /// <param name="signedProof">The proof presented to ourselves that needs to be verified</param>
        /// <param name="publicKeyCspBlob">The CSP Blob the trusted WOPI client is thought to have used to sign the proof source</param>
        /// <returns></returns>
        private bool IsProven(byte[] expectedProof, string signedProof, string? publicKeyCspBlob)
        {
            Debug.Assert(expectedProof is object && 0 < expectedProof.Length);
            Debug.Assert(!string.IsNullOrWhiteSpace(signedProof));

            if (string.IsNullOrWhiteSpace(publicKeyCspBlob)) throw new ArgumentNullException(nameof(publicKeyCspBlob));

            const bool HAS_NOT_BEEN_VERIFIED = false;

            const string SHA256 = "SHA256";

            var publicKey = Convert.FromBase64String(publicKeyCspBlob);

            var proof = Convert.FromBase64String(signedProof);

            try
            {
                using var rsaAlgorithm = new RSACryptoServiceProvider();

                rsaAlgorithm.ImportCspBlob(publicKey);

                return rsaAlgorithm.VerifyData(expectedProof, SHA256, proof);
            }
            catch (FormatException) { return HAS_NOT_BEEN_VERIFIED; }
            catch (CryptographicException) { return HAS_NOT_BEEN_VERIFIED; }
        }
    }
}
