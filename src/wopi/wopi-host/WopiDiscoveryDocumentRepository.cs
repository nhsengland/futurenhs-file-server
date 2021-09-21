using FutureNHS.WOPIHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FutureNHS.WOPIHost
{
    public interface IWopiDiscoveryDocumentRepository
    {
        Task<IWopiDiscoveryDocument> GetAsync(CancellationToken cancellationToken);
    }

    public sealed class WopiDiscoveryDocumentRepository : IWopiDiscoveryDocumentRepository
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WopiDiscoveryDocumentRepository> _logger;
        private readonly WopiConfiguration _wopiConfiguration;

        public WopiDiscoveryDocumentRepository(IHttpClientFactory httpClientFactory, IOptionsSnapshot<WopiConfiguration> wopiConfiguration, ILogger<WopiDiscoveryDocumentRepository> logger)
        {
            _logger = logger;

            _httpClientFactory = httpClientFactory        ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _wopiConfiguration = wopiConfiguration?.Value ?? throw new ArgumentNullException(nameof(wopiConfiguration));
        }

        async Task<IWopiDiscoveryDocument> IWopiDiscoveryDocumentRepository.GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ClientDiscoveryDocumentUrl = _wopiConfiguration.ClientDiscoveryDocumentUrl;

            if (string.IsNullOrWhiteSpace(ClientDiscoveryDocumentUrl)) return WopiDiscoveryDocument.Empty;
            if (!Uri.IsWellFormedUriString(ClientDiscoveryDocumentUrl, UriKind.Absolute)) return WopiDiscoveryDocument.Empty;

            var discoveryDocumentUrl = new Uri(ClientDiscoveryDocumentUrl, UriKind.Absolute);

            var httpClient = _httpClientFactory.CreateClient("wopi-discovery-document");

            using var request = new HttpRequestMessage(HttpMethod.Get, discoveryDocumentUrl);

            var xmlMediaTypes = new[] { "application/xml", "text/xml" };

            var accepts = xmlMediaTypes.Aggregate(string.Empty, (acc, n) => string.Concat(acc, n, ", "))[0..^2];

            request.Headers.Add("Accept", accepts);

            try
            {               
                using var response = await httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode) return WopiDiscoveryDocument.Empty;

                var contentType = response.Content?.Headers.ContentType.MediaType.Trim();

                if (string.IsNullOrWhiteSpace(contentType)) return WopiDiscoveryDocument.Empty;

                if (!accepts.Contains(contentType, StringComparison.OrdinalIgnoreCase)) return WopiDiscoveryDocument.Empty;

                var httpContent = response.Content;

                if (httpContent is null) return WopiDiscoveryDocument.Empty;

                using var strm = await httpContent.ReadAsStreamAsync();

                var xml = await XDocument.LoadAsync(strm, LoadOptions.None, cancellationToken);

                if (WopiDiscoveryDocument.IsXmlDocumentSupported(xml)) return new WopiDiscoveryDocument(discoveryDocumentUrl, xml, _logger);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError(ex, "Failed to connect to the WOPI Client to download the discovery document");
            }

            return WopiDiscoveryDocument.Empty;
        }
    }
}
