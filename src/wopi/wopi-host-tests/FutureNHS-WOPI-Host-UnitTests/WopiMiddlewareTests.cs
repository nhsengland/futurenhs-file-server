using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.Handlers;
using FutureNHS_WOPI_Host_UnitTests.Stubs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests
{
    [TestClass]
    public sealed class WopiMiddlewareTests
    {
        [TestMethod]
        public void CTor_DoesNotThrowIfNextItemInPipelineIsNull()
        {
            new WopiMiddleware(next: default);
        }



        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task Invoke_ThrowsIfHttpContextIsNull()
        {
            var wopiMiddleware = new WopiMiddleware(next: default);

            await wopiMiddleware.Invoke(httpContext: default);
        }

        [TestMethod]
        public async Task Invoke_CallsNextMiddlewareInPipelineIfInjected()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();


            var services = new ServiceCollection();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped<WopiRequestFactory>();
            services.AddScoped<IWopiRequestFactory>(sp => sp.GetRequiredService<WopiRequestFactory>());
            services.AddScoped(sp => new Moq.Mock<IFileRepository>().Object);

            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var hasBeenInvoked = false;

            var wopiMiddleware = new WopiMiddleware(_ => { hasBeenInvoked = true; return Task.CompletedTask; });

            await wopiMiddleware.Invoke(httpContext);

            Assert.IsTrue(hasBeenInvoked, "Expected Wopi Middleware to execute the next item in the pipeline after it completes its work");
        }



        [TestMethod]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task Invoke_ProcessRequest_ThrowsWhenCancellationTokenCancelled()
        {
            using var cts = new CancellationTokenSource();

            cts.Cancel();

            var httpContext = new DefaultHttpContext() { RequestAborted = cts.Token };

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);
        }

        [TestMethod]
        public async Task Invoke_ProcessRequest_DefersToWopiRequestFactoryToIdentifyWopiRequests()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var services = new ServiceCollection();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            var requestFactoryInvoked = false;

            var wopiRequestFactory = new Moq.Mock<IWopiRequestFactory>();

            var emptyWopiRequest = WopiRequest.EMPTY;

            wopiRequestFactory.Setup(x => x.TryCreateRequest(Moq.It.IsAny<HttpRequest>(), out emptyWopiRequest)).Returns(false).Callback(() => { requestFactoryInvoked = true; });

            services.AddScoped(sp => wopiRequestFactory.Object);

            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);

            Assert.IsTrue(requestFactoryInvoked, "Expected the wopi request factory to have been deferred to such that it could identify if the request was WOPI related or not");
        }


        delegate void TryCreateRequestDelegate(HttpRequest httpRequest, out WopiRequest wopiRequest);

        [TestMethod]
        public async Task Invoke_ProcessRequest_DefersToWopiProofCheckerToVerifyPresentedProofs()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var wopiRequestStub = new WopiRequestStub((_, __) => Task.CompletedTask);

            var wopiRequestFactory = new Moq.Mock<IWopiRequestFactory>();

            var wopiRequest = default(WopiRequest);

            wopiRequestFactory.Setup(x => x.TryCreateRequest(Moq.It.IsAny<HttpRequest>(), out wopiRequest)).
                Callback(new TryCreateRequestDelegate((HttpRequest httpRequest, out WopiRequest _) => _ = wopiRequestStub)).
                Returns(true);

            var proofCheckerInvoked = false;

            var wopiCryptoProofChecker = new Moq.Mock<IWopiCryptoProofChecker>();

            wopiCryptoProofChecker.Setup(x => x.IsProofInvalid(Moq.It.IsAny<HttpRequest>(), Moq.It.IsAny<IWopiProofKeysProvider>())).Returns((false, false)).Callback(() => { proofCheckerInvoked = true; });

            var wopiDiscoveryDocumentRepository = new Moq.Mock<IWopiDiscoveryDocumentRepository>();

            var services = new ServiceCollection();

            services.AddMemoryCache();
            services.AddHttpClient();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped(sp => wopiDiscoveryDocumentRepository.Object);
            services.AddScoped(sp => wopiRequestFactory.Object);
            services.AddScoped(sp => wopiCryptoProofChecker.Object);

            services.AddScoped<WopiDiscoveryDocumentFactory>();
            services.AddScoped<IWopiDiscoveryDocumentFactory>(sp => sp.GetRequiredService<WopiDiscoveryDocumentFactory>());

            var serviceProvider = services.BuildServiceProvider();

            var memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();

            var cachedWopiDiscoveryDocument = new Moq.Mock<IWopiDiscoveryDocument>();

            cachedWopiDiscoveryDocument.SetupGet(x => x.IsTainted).Returns(false);

            memoryCache.Set(ExtensionMethods.WOPI_DISCOVERY_DOCUMENT_CACHE_KEY, cachedWopiDiscoveryDocument.Object);

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=tokengoeshere");

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);

            Assert.IsTrue(proofCheckerInvoked, "Expected the wopi proof checker to have been deferred to such that it could validate the proof presented in the request");
        }

        [TestMethod]
        public async Task Invoke_ProcessRequest_TransparentlyIgnoresNoneWopiRequests()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var services = new ServiceCollection();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped<WopiRequestFactory>();
            services.AddScoped<IWopiRequestFactory>(sp => sp.GetRequiredService<WopiRequestFactory>());

            services.AddScoped(sp => new Moq.Mock<IFileRepository>().Object);

            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);
        }

        [TestMethod]
        public async Task Invoke_ProcessRequest_InvokesRequestHandlerIfTheProofIsVerified()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var wopiRequestHandlerInvoked = false;

            var wopiRequestStub = new WopiRequestStub((_, __) => { wopiRequestHandlerInvoked = true; return Task.CompletedTask; });

            var wopiRequestFactory = new Moq.Mock<IWopiRequestFactory>();

            var wopiRequest = default(WopiRequest);

            wopiRequestFactory.Setup(x => x.TryCreateRequest(Moq.It.IsAny<HttpRequest>(), out wopiRequest)).
                Callback(new TryCreateRequestDelegate((HttpRequest httpRequest, out WopiRequest _) => _ = wopiRequestStub)).
                Returns(true);

            var wopiCryptoProofChecker = new Moq.Mock<IWopiCryptoProofChecker>();

            wopiCryptoProofChecker.Setup(x => x.IsProofInvalid(Moq.It.IsAny<HttpRequest>(), Moq.It.IsAny<IWopiProofKeysProvider>())).Returns((false, false));

            var wopiDiscoveryDocumentRepository = new Moq.Mock<IWopiDiscoveryDocumentRepository>();

            var services = new ServiceCollection();

            services.AddMemoryCache();
            services.AddHttpClient();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped(sp => wopiDiscoveryDocumentRepository.Object);
            services.AddScoped(sp => wopiRequestFactory.Object);
            services.AddScoped(sp => wopiCryptoProofChecker.Object);

            services.AddScoped<WopiDiscoveryDocumentFactory>();
            services.AddScoped<IWopiDiscoveryDocumentFactory>(sp => sp.GetRequiredService<WopiDiscoveryDocumentFactory>());

            var serviceProvider = services.BuildServiceProvider();

            var memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();

            var cachedWopiDiscoveryDocument = new Moq.Mock<IWopiDiscoveryDocument>();

            cachedWopiDiscoveryDocument.SetupGet(x => x.IsTainted).Returns(false);

            memoryCache.Set(ExtensionMethods.WOPI_DISCOVERY_DOCUMENT_CACHE_KEY, cachedWopiDiscoveryDocument.Object);

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=tokengoeshere");

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);

            Assert.IsTrue(wopiRequestHandlerInvoked, "Expected the request handler supplied by the factory to be invoked");
        }

        [TestMethod]
        [ExpectedException(typeof(ApplicationException))]
        public async Task Invoke_ProcessRequest_ThrowsIfOfferedProofIsNotVerifiedToBeAuthentic()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var wopiCryptoProofChecker = new Moq.Mock<IWopiCryptoProofChecker>();

            wopiCryptoProofChecker.Setup(x => x.IsProofInvalid(Moq.It.IsAny<HttpRequest>(), Moq.It.IsAny<IWopiProofKeysProvider>())).Returns((true, false));

            var wopiDiscoveryDocumentRepository = new Moq.Mock<IWopiDiscoveryDocumentRepository>();

            var fileRepository = new Moq.Mock<IFileRepository>();

            var services = new ServiceCollection();

            services.AddMemoryCache();
            services.AddHttpClient();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped<WopiRequestFactory>();
            services.AddScoped<IWopiRequestFactory>(sp => sp.GetRequiredService<WopiRequestFactory>());

            services.AddScoped<WopiDiscoveryDocumentFactory>();
            services.AddScoped<IWopiDiscoveryDocumentFactory>(sp => sp.GetRequiredService<WopiDiscoveryDocumentFactory>());

            services.AddScoped(sp => wopiCryptoProofChecker.Object);
            services.AddScoped(sp => wopiDiscoveryDocumentRepository.Object);
            services.AddScoped(sp => fileRepository.Object);

            var serviceProvider = services.BuildServiceProvider();

            var memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();

            var cachedWopiDiscoveryDocument = new Moq.Mock<IWopiDiscoveryDocument>();

            cachedWopiDiscoveryDocument.SetupGet(x => x.IsTainted).Returns(false);

            memoryCache.Set(ExtensionMethods.WOPI_DISCOVERY_DOCUMENT_CACHE_KEY, cachedWopiDiscoveryDocument.Object);

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=tokengoeshere");

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);
        }

        [TestMethod]
        [ExpectedException(typeof(ApplicationException))]
        public async Task Invoke_ProcessRequest_ThrowsIfDiscoveryDocumentCannotBeLocatedToExtractProofKeys()
        {
            var configurationData = new Dictionary<string, string>();

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddInMemoryCollection(configurationData);

            var configuration = configurationBuilder.Build();

            var wopiDiscoveryDocumentFactory = new Moq.Mock<IWopiDiscoveryDocumentFactory>();

            wopiDiscoveryDocumentFactory.Setup(x => x.CreateDocumentAsync(Moq.It.IsAny<CancellationToken>())).Returns(Task.FromResult< IWopiDiscoveryDocument>(WopiDiscoveryDocument.Empty));

            var fileRepository = new Moq.Mock<IFileRepository>();

            var services = new ServiceCollection();

            services.AddMemoryCache();
            services.AddHttpClient();

            services.Configure<Features>(configuration.GetSection("FeatureManagement"));

            services.AddScoped<WopiRequestFactory>();
            services.AddScoped<IWopiRequestFactory>(sp => sp.GetRequiredService<WopiRequestFactory>());

            services.AddScoped(sp => wopiDiscoveryDocumentFactory.Object);
            services.AddScoped(sp => fileRepository.Object);

            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext() { RequestServices = serviceProvider };

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=tokengoeshere");

            var wopiMiddleware = new WopiMiddleware(default);

            await wopiMiddleware.Invoke(httpContext);
        }
    }
}
