using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using File = FutureNHS.WOPIHost.File;

namespace FutureNHS_WOPI_Host_UnitTests.Handlers
{
    [TestClass]
    public sealed class GetFileWopiRequestTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void With_ThrowsIfFileIsEmpty()
        {
            GetFileWopiRequest.With(File.EMPTY, accessToken: "access-token");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void With_ThrowsIfAccessTokenIsNull()
        {
            var file = File.With("file-name", "file-version");

            GetFileWopiRequest.With(file, accessToken: default);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void With_ThrowsIfAccessTokenIsEmpty()
        {
            var file = File.With("file-name", "file-version");

            GetFileWopiRequest.With(file, accessToken: string.Empty);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void With_ThrowsIfAccessTokenIsWhitespace()
        {
            var file = File.With("file-name", "file-version");

            GetFileWopiRequest.With(file, accessToken: " ");
        }

        [TestMethod]
        public void With_DoesNotThrowIfAccessTokenIsNeitherNullNorWhitespace()
        {
            var file = File.With("file-name", "file-version");

            GetFileWopiRequest.With(file, accessToken: "access-token");
        }



        [TestMethod]
        [DataRow("Excel-Spreadsheet.xlsx")]
        [DataRow("Image-File.jpg")]
        [DataRow("OpenDocument-Text-File.odt")]
        [DataRow("Portable-Document-Format-File.pdf")]
        [DataRow("PowerPoint-Presentation.pptx")]
        [DataRow("Text-File.txt")]
        [DataRow("Word-Document.docx")]
        public async Task HandleAsync_ResolvesAndWritesFileCorrectlyToGivenStream(string fileName)
        {
            var cancellationToken = new CancellationToken();

            var httpContext = new DefaultHttpContext();

            var contentRootPath = Environment.CurrentDirectory;

            var filePath = Path.Combine(contentRootPath, "Files", fileName);

            Assert.IsTrue(System.IO.File.Exists(filePath), $"Expected the {fileName} file to be accessible in the test environment");

            var fileInfo = new FileInfo(filePath);

            var fileBuffer = await System.IO.File.ReadAllBytesAsync(filePath, cancellationToken);

            using var responseBodyStream = new MemoryStream(fileBuffer.Length);
            
            httpContext.Response.Body = responseBodyStream;

            var fileRepository = new Moq.Mock<IFileRepository>();

            var fileRepositoryInvoked = false;

            var services = new ServiceCollection();

            services.AddScoped(sp => fileRepository.Object);

            httpContext.RequestServices = services.BuildServiceProvider();

            var fileVersion = Guid.NewGuid().ToString();

            using var algo = MD5.Create();

            var contentHash = algo.ComputeHash(fileBuffer);

            var fileMetadata = new FileMetadata("title", "description", "group-name", fileVersion, "owner", fileName, fileInfo.Extension, (ulong)fileInfo.Length, "blobName", DateTimeOffset.UtcNow, Convert.ToBase64String(contentHash), FileStatus.Verified);

            var fileWriteDetails = new FileWriteDetails(fileVersion, "content-type", contentHash, (ulong)fileBuffer.Length, "content-encoding", "content-language", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, fileMetadata);

            fileRepository.
                Setup(x => x.WriteToStreamAsync(Moq.It.IsAny<FileMetadata>(), Moq.It.IsAny<Stream>(), Moq.It.IsAny<CancellationToken>())).
                Callback(async (FileMetadata givenFileMetadata, Stream givenStream, CancellationToken givenCancellationToken) => {

                    Assert.IsFalse(givenFileMetadata.IsEmpty);
                    Assert.IsNotNull(givenStream);

                    Assert.IsFalse(givenCancellationToken.IsCancellationRequested, "Expected the cancellation token to not be cancelled");

                    Assert.AreSame(responseBodyStream, givenStream, "Expected the SUT to as the repository to write the file to the stream it was asked to");
                    Assert.AreSame(fileName, givenFileMetadata.Name, "Expected the SUT to request the file from the repository whose name it was provided with");
                    Assert.AreSame(fileVersion, givenFileMetadata.Version, "Expected the SUT to request the file version from the repository that it was provided with");
                    Assert.AreEqual(cancellationToken, givenCancellationToken, "Expected the same cancellation token to propagate between service interfaces");

                    await givenStream.WriteAsync(fileBuffer, cancellationToken);
                    await givenStream.FlushAsync(cancellationToken);

                    fileRepositoryInvoked = true;
                }).
                Returns(Task.FromResult(fileWriteDetails));

            fileRepository.Setup(x => x.GetMetadataAsync(Moq.It.IsAny<File>(), Moq.It.IsAny<CancellationToken>())).Returns(Task.FromResult(fileMetadata));

            var accessToken = Guid.NewGuid().ToString();

            var file = File.With(fileName, fileVersion);

            var getFileWopiRequest = GetFileWopiRequest.With(file, accessToken);

            await getFileWopiRequest.HandleAsync(httpContext, cancellationToken);

            Assert.IsTrue(fileRepositoryInvoked, "Expected the SUT to defer to the file repository with the correct parameters");

            Assert.AreEqual(fileBuffer.Length, responseBodyStream.Length, "All bytes in the file should be written to the target stream");

            Assert.IsTrue(httpContext.Response.Headers.ContainsKey("X-WOPI-ItemVersion"), "Expected the X-WOPI-ItemVersion header to have been written to the response");

            Assert.IsNotNull(httpContext.Response.Headers["X-WOPI-ItemVersion"], "Expected the X-WOPI-ItemVersion header in the response to not be null");
        }
    }
}
