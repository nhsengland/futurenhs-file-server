using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace FutureNHS_WOPI_Host_UnitTests
{
    [TestClass]
    public sealed class WopiRequestFactoryTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_ThrowsIfFeaturesOptionsConfigurationIsNull()
        {
            _ = new WopiRequestFactory(features: default);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_ThrowsIfFeaturesConfigurationIsNull()
        {
            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(default(Features));

            _ = new WopiRequestFactory(features: snapshot.Object);
        }



        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CreateRequest_ThrowsIfHttpRequestIsNull()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            _ = wopiRequestFactory.TryCreateRequest(request: default, out _);
        }

        [TestMethod]
        public void CreateRequest_NoneWopiRequestIsIdentifiedAndIgnored()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsFalse(createdOk);

            Assert.IsNotNull(wopiRequest);
            
            Assert.IsTrue(wopiRequest.IsEmpty, "Expected a none WOPI request to be ignored and return an empty marker");
        }

        [TestMethod]
        public void CreateRequest_WopiRequestWithMissingAccessTokenIsIdentifiedAndIgnored()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsFalse(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsTrue(wopiRequest.IsEmpty, "Expected a WOPI request with a missing access token to be ignored and return an empty marker");
        }

        [TestMethod]
        public void CreateRequest_WopiRequestWithInvalidAccessTokenIsIdentifiedAndIgnored()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=<invalid-access-token>");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsFalse(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsTrue(wopiRequest.IsEmpty, "Expected a WOPI request with an invalid access token to be ignored and return an empty marker");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesCheckFileInfoRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(CheckFileInfoWopiRequest), "Expected Check File Info requests to be identified");

            var checkFileInfoRequest = (CheckFileInfoWopiRequest)wopiRequest;

            Assert.AreEqual("<valid-access-token>", checkFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesGetFileRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(GetFileWopiRequest), "Expected Get File requests to be identified");

            var getFileInfoRequest = (GetFileWopiRequest)wopiRequest;

            Assert.AreEqual("<valid-access-token>", getFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesEphemeralRedirectRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>&ephemeral_redirect=true");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(RedirectToFileStoreRequest), "Expected Redirect to File Store requests to be identified");

            var getFileInfoRequest = (RedirectToFileStoreRequest)wopiRequest;

            Assert.AreEqual("<valid-access-token>", getFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesSaveFileRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Post;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(PostFileWopiRequest), "Expected Save File requests to be identified");

            var postFileInfoRequest = (PostFileWopiRequest)wopiRequest;

            Assert.AreEqual("<valid-access-token>", postFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesAndIgnoresFolderRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestFactory wopiRequestFactory = new WopiRequestFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Post;
            httpRequest.Path = "/wopi/folders/";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            var createdOk = wopiRequestFactory.TryCreateRequest(request: httpContext.Request, out var wopiRequest);

            Assert.IsFalse(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsTrue(wopiRequest.IsEmpty, "Expected folder based requests to be ignored");
        }
    }
}
