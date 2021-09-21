using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Dynamic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests.Handlers
{
    [TestClass]
    public sealed class CheckFileInfoWopiRequestTests
    {
        [TestMethod]
        [DataRow("file-title1", "file-description1", "group1", "version1", "owner1", "Excel-Spreadsheet.xlsx", ".xlsx", ulong.MaxValue, "hash")]
        [DataRow("file-title2", "file-description2", "group2", "version2", "owner2", "Image-File.jpg", ".jpg", ulong.MaxValue, "hash")]
        [DataRow("file-title3", "file-description3", "group3", "version3", "owner3", "OpenDocument-Text-File.odt", ".odt", ulong.MaxValue, "hash")]
        [DataRow("file-title4", "file-description4", "group4", "version4", "owner4", "Portable-Document-Format-File.pdf", ".pdf", ulong.MaxValue, "hash")]
        [DataRow("file-title5", "file-description5", "group5", "version5", "owner5", "PowerPoint-Presentation.pptx", ".pptx", ulong.MaxValue, "hash")]
        [DataRow("file-title6", "file-description6", "group6", "version6", "owner6", "Text-File.txt", ".txt", ulong.MaxValue, "hash")]
        [DataRow("file-title7", "file-description7", "group7", "version7", "owner7", "Word-Document.docx", ".docx", ulong.MaxValue, "hash")]
        public async Task HandleAsync_FormsWOPICompliantResponseUsingFileMetadataAndUserContextAndFeatures(string title, string description, string groupName, string version, string owner, string fileName, string extension, ulong sizeInBytes, string contentHash)
        {
            var cancellationToken = new CancellationToken();

            var services = new ServiceCollection();

            var fileRepository = new Moq.Mock<IFileRepository>();

            var fileRepositoryInvoked = false;

            services.AddScoped(sp => fileRepository.Object);

            var httpContext = new DefaultHttpContext {
                RequestServices = services.BuildServiceProvider()
            };

            using var responseBodyStream = new MemoryStream();

            httpContext.Response.Body = responseBodyStream;

            var fileVersion = Guid.NewGuid().ToString();

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

            fileRepository.
                Setup(x => x.GetMetadataAsync(Moq.It.IsAny<FutureNHS.WOPIHost.File>(), Moq.It.IsAny<CancellationToken>())).
                Callback((FutureNHS.WOPIHost.File givenFile, CancellationToken givenCancellationToken) => {

                    Assert.IsFalse(givenFile.IsEmpty);

                    Assert.IsFalse(givenCancellationToken.IsCancellationRequested, "Expected the cancellation token to not be cancelled");

                    Assert.AreSame(fileName, givenFile.Name, "Expected the SUT to request the file from the repository whose name it was provided with");
                    Assert.AreSame(fileVersion, givenFile.Version, "Expected the SUT to request the file version from the repository that it was provided with");
                    Assert.AreEqual(cancellationToken, givenCancellationToken, "Expected the same cancellation token to propagate between service interfaces");

                    fileRepositoryInvoked = true;
                }).
                Returns(Task.FromResult(fileMetadata));

            var ephemeralDownloadLink = new Uri("https://www.file-storage.com/files/file_id", UriKind.Absolute);

            fileRepository.Setup(x => x.GeneratePrivateEphemeralDownloadLink(fileMetadata, Moq.It.IsAny<CancellationToken>())).Returns(Task.FromResult(ephemeralDownloadLink));

            var features = new Features();

            var accessToken = Guid.NewGuid().ToString();

            var file = FutureNHS.WOPIHost.File.With(fileName, fileVersion);

            var checkFileInfoWopiRequest = CheckFileInfoWopiRequest.With(file, accessToken, features);

            await checkFileInfoWopiRequest.HandleAsync(httpContext, cancellationToken);

            Assert.IsTrue(fileRepositoryInvoked);

            Assert.AreEqual("application/json", httpContext.Response.ContentType);

            Assert.AreSame(responseBodyStream, httpContext.Response.Body);

            responseBodyStream.Position = 0;

            dynamic responseBody = await JsonSerializer.DeserializeAsync<ExpandoObject>(responseBodyStream, cancellationToken: cancellationToken);

            Assert.IsNotNull(responseBody);

            Assert.AreEqual(fileMetadata.Title, ((JsonElement)(responseBody.BaseFileName)).GetString());
            Assert.AreEqual(fileMetadata.Version, ((JsonElement)(responseBody.Version)).GetString());
            Assert.AreEqual(fileMetadata.Owner, ((JsonElement)(responseBody.OwnerId)).GetString());
            Assert.AreEqual(fileMetadata.Extension, ((JsonElement)(responseBody.FileExtension)).GetString());
            Assert.AreEqual(fileMetadata.SizeInBytes, ((JsonElement)(responseBody.Size)).GetUInt64());
            Assert.AreEqual(ephemeralDownloadLink.AbsoluteUri, ((JsonElement)(responseBody.FileUrl)).GetString());
            Assert.AreEqual(fileMetadata.LastWriteTime.ToIso8601(), ((JsonElement)(responseBody.LastModifiedTime)).GetString());

            Assert.AreEqual(FutureNHS.WOPIHost.File.FILENAME_MAXIMUM_LENGTH, ((JsonElement)(responseBody.FileNameMaxLength)).GetInt32());
        }

        // TODO - Tests needed to verify correct propagation of data from owner context and feature flag configuration
    }
}
