using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.WOPIRequests;
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
            _ = new WopiRequestHandlerFactory(features: default);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_ThrowsIfFeaturesConfigurationIsNull()
        {
            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(default(Features));

            _ = new WopiRequestHandlerFactory(features: snapshot.Object);
        }



        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CreateRequest_ThrowsIfHttpRequestIsNull()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            _ = wopiRequestFactory.TryCreateRequestHandler(request: default, out _);
        }

        [TestMethod]
        public void CreateRequest_NoneWopiRequestIsIdentifiedAndIgnored()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

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

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

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

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

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

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(CheckFileInfoWopiRequestHandler), "Expected Check File Info requests to be identified");

            var checkFileInfoRequest = (CheckFileInfoWopiRequestHandler)wopiRequest;

            Assert.AreEqual("<valid-access-token>", checkFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesGetFileRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(GetFileWopiRequestHandler), "Expected Get File requests to be identified");

            var getFileInfoRequest = (GetFileWopiRequestHandler)wopiRequest;

            Assert.AreEqual("<valid-access-token>", getFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesEphemeralRedirectRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Get;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>&ephemeral_redirect=true");

            httpRequest.Headers["X-WOPI-ItemVersion"] = "file-version";

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(RedirectToFileStoreRequestHandler), "Expected Redirect to File Store requests to be identified");

            var getFileInfoRequest = (RedirectToFileStoreRequestHandler)wopiRequest;

            Assert.AreEqual("<valid-access-token>", getFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesSaveFileRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Post;
            httpRequest.Path = "/wopi/files/file-name|file-version/contents";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(PostFileWopiRequestHandler), "Expected Save File requests to be identified");

            var postFileInfoRequest = (PostFileWopiRequestHandler)wopiRequest;

            Assert.AreEqual("<valid-access-token>", postFileInfoRequest.AccessToken, "Expected the access token to be extracted and retained");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesAuthUserRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Post;
            httpRequest.Path = "/wopi/files/file-name|file-version/authorise-user";
            httpRequest.QueryString = new QueryString("?permission=view");

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsTrue(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsInstanceOfType(wopiRequest, typeof(PostAuthoriseUserRequestHandler), "Expected Authorise User requests to be identified");

            var postAuthoriseUserRequestHandler = (PostAuthoriseUserRequestHandler)wopiRequest;

            Assert.IsNotNull(postAuthoriseUserRequestHandler.AccessToken, "Expected the access token to be created for the user");
            Assert.AreEqual(PostAuthoriseUserRequestHandler.FileAccessPermissions.View, postAuthoriseUserRequestHandler.Permission, "Expected the user to be granted view permissions");
        }

        [TestMethod]
        public void CreateRequest_IdentifiesAndIgnoresFolderRequest()
        {
            var features = new Features();

            var snapshot = new Moq.Mock<IOptionsSnapshot<Features>>();

            snapshot.SetupGet(x => x.Value).Returns(features);

            IWopiRequestHandlerFactory wopiRequestFactory = new WopiRequestHandlerFactory(features: snapshot.Object);

            var httpContext = new DefaultHttpContext();

            var httpRequest = httpContext.Request;

            httpRequest.Method = HttpMethods.Post;
            httpRequest.Path = "/wopi/folders/";
            httpRequest.QueryString = new QueryString("?access_token=<valid-access-token>");

            var createdOk = wopiRequestFactory.TryCreateRequestHandler(request: httpContext.Request, out var wopiRequest);

            Assert.IsFalse(createdOk);

            Assert.IsNotNull(wopiRequest);

            Assert.IsTrue(wopiRequest.IsEmpty, "Expected folder based requests to be ignored");
        }
    }
}
