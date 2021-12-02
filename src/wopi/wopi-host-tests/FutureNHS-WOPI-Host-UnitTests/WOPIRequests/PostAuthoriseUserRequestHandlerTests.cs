using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.WOPIRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Dynamic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests.WOPIRequests
{
    [TestClass]
    public sealed class PostAuthoriseUserRequestHandlerTests
    {

        [TestMethod]
        [DataRow("file-title1", "file-description1", "group1", "version1", "owner1", "Excel-Spreadsheet.xlsx", ".xlsx", ulong.MaxValue, "hash")]
        [DataRow("file-title2", "file-description2", "group2", "version2", "owner2", "Image-File.jpg", ".jpg", ulong.MaxValue, "hash")]
        [DataRow("file-title3", "file-description3", "group3", "version3", "owner3", "OpenDocument-Text-File.odt", ".odt", ulong.MaxValue, "hash")]
        [DataRow("file-title4", "file-description4", "group4", "version4", "owner4", "Portable-Document-Format-File.pdf", ".pdf", ulong.MaxValue, "hash")]
        [DataRow("file-title5", "file-description5", "group5", "version5", "owner5", "PowerPoint-Presentation.pptx", ".pptx", ulong.MaxValue, "hash")]
        [DataRow("file-title6", "file-description6", "group6", "version6", "owner6", "Text-File.txt", ".txt", ulong.MaxValue, "hash")]
        [DataRow("file-title7", "file-description7", "group7", "version7", "owner7", "Word-Document.docx", ".docx", ulong.MaxValue, "hash")]
        public async Task HandleAsync_(string title, string description, string groupName, string version, string owner, string fileName, string extension, ulong sizeInBytes, string contentHash)
        {
            var cancellationToken = CancellationToken.None;

            var file = FutureNHS.WOPIHost.File.With(fileName, version);

            var services = new ServiceCollection();

            var endpointForFileExtension = new Uri("https://domain.com/path?param=value", UriKind.Absolute);

            var wopiDiscoveryDocument = new Moq.Mock<IWopiDiscoveryDocument>();

            wopiDiscoveryDocument.Setup(o => o.GetEndpointForFileExtension(extension, "view", Moq.It.IsAny<Uri>())).Returns(endpointForFileExtension);
            wopiDiscoveryDocument.Setup(o => o.IsEmpty).Returns(false);
            wopiDiscoveryDocument.Setup(o => o.IsTainted).Returns(false);

            var wopiDiscoveryDocumentFactory = new Moq.Mock<IWopiDiscoveryDocumentFactory>();

            wopiDiscoveryDocumentFactory.Setup(o => o.CreateDocumentAsync(cancellationToken)).Returns(Task.FromResult(wopiDiscoveryDocument.Object));

            var fileMetadata = new FileMetadata(
                title: title,
                description: description,
                groupName: groupName,
                version: version,
                owner: owner,
                name: fileName,
                extension: extension,
                blobName: fileName,
                sizeInBytes: sizeInBytes,
                lastWriteTime: DateTimeOffset.UtcNow,
                contentHash: contentHash,
                fileStatus: FileStatus.Verified
                );

            var fileMetadataProvider = new Moq.Mock<IFileMetadataProvider>();

            fileMetadataProvider.Setup(o => o.GetForFileAsync(file, cancellationToken)).Returns(Task.FromResult(fileMetadata));

            var wopiConfiguration = new WopiConfiguration {
                HostFilesUrl = "https://hostfiles.net/path"
            };

            var wopiConfigurationSnapshot = new Moq.Mock<IOptionsSnapshot<WopiConfiguration>>();

            wopiConfigurationSnapshot.Setup(o => o.Value).Returns(wopiConfiguration);

            services.AddScoped(sp => wopiDiscoveryDocumentFactory.Object);
            services.AddScoped(sp => fileMetadataProvider.Object);
            services.AddScoped(sp => wopiConfigurationSnapshot.Object);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };

            using var responseBodyStream = new MemoryStream();

            httpContext.Response.Body = responseBodyStream;

            var userAuthToken = Guid.NewGuid().ToString();

            var permission = PostAuthoriseUserRequestHandler.FileAccessPermissions.View;

            var postAuthoriseUserRequestHandler = PostAuthoriseUserRequestHandler.With(userAuthToken, permission, file);



            await postAuthoriseUserRequestHandler.HandleAsync(httpContext, cancellationToken);

            Assert.AreEqual("application/json; charset=utf-8", httpContext.Response.ContentType);

            Assert.AreSame(responseBodyStream, httpContext.Response.Body);

            responseBodyStream.Position = 0;

            dynamic responseBody = await JsonSerializer.DeserializeAsync<ExpandoObject>(responseBodyStream, cancellationToken: cancellationToken);

            Assert.IsNotNull(responseBody);

            var accessToken = ((JsonElement)(responseBody.accessToken)).GetString();

            Assert.IsNotNull(accessToken);

            var wopiClientUrlForFile = ((JsonElement)(responseBody.wopiClientUrlForFile)).GetString();

            Assert.IsTrue(Uri.IsWellFormedUriString(wopiClientUrlForFile, UriKind.Absolute));

            Assert.AreEqual(endpointForFileExtension, new Uri(wopiClientUrlForFile, UriKind.Absolute));
        }
    }
}
