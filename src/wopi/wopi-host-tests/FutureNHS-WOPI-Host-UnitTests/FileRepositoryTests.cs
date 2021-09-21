using FutureNHS.WOPIHost;
using FutureNHS.WOPIHost.Configuration;
using FutureNHS.WOPIHost.PlatformHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests
{
    [TestClass]
    public sealed class FileRepositoryTests
    {
        // Given the dependency on Azure Storage, the system under test is difficult to cover in full without taking a dependency
        // on running emulators to serve/save files (which we cannot do in an Azure Pipeline) so the following tests are added to 
        // run in a debug (local) build

#if DEBUG
        [TestMethod]
        public async Task WriteToStreamAsync_SanityCheckForLocalDevOnly()
        {
            var cancellationToken = new CancellationToken();

            var optionsAccessor = new Moq.Mock<IOptions<MemoryCacheOptions>>();

            optionsAccessor.Setup(x => x.Value).Returns(new MemoryCacheOptions());

            var memoryCache = new MemoryCache(optionsAccessor.Object);

            var logger = new Moq.Mock<ILogger<FileRepository>>().Object;
            
            var clock = new SystemClock();

            var configurationBuilder = new ConfigurationBuilder();

            // NB - Given the SUT is actually connecting to blob storage and a sql db, the connection strings etc are stored in a 
            //      local secrets file that is not included in source control.  If running these tests locally, ensure this file is 
            //      present in your project and that it contains the entries we need

            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

            var configuration = configurationBuilder.Build();

            var azurePlatformConfiguration = new AzurePlatformConfiguration()
            { 
                AzureBlobStorage = new AzureBlobStorageConfiguration() { ContainerName = configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:ContainerName") }
            };

            var azurePlatformConfigurationOptionsSnapshot = new Moq.Mock<IOptionsSnapshot<AzurePlatformConfiguration>>();

            azurePlatformConfigurationOptionsSnapshot.Setup(x => x.Value).Returns(azurePlatformConfiguration);

            var primaryServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:PrimaryServiceUrl"), UriKind.Absolute);
            var geoRedundantServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:GeoRedundantServiceUrl"), UriKind.Absolute);

            var azureBlobStorageClient = new AzureBlobStoreClient(primaryServiceUrl, geoRedundantServiceUrl, memoryCache, clock, default);

            var readWriteConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadWriteConnectionString");
            var readOnlyConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadOnlyConnectionString");

            var sqlLogger = new Moq.Mock<ILogger<AzureSqlClient>>().Object;

            var sqlCnFactoryLogger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var sqlDbConnectionFactory = new AzureSqlDbConnectionFactory(readWriteConnectionString, readOnlyConnectionString, sqlCnFactoryLogger);

            var azureSqlClient = new AzureSqlClient(sqlDbConnectionFactory, sqlLogger);

            IFileRepository fileRepository = new FileRepository(azureBlobStorageClient, azureSqlClient, azurePlatformConfigurationOptionsSnapshot.Object, logger);

            var blobName = "4d6fa0f8-34a7-4f34-922f-8b06416097e1.pdf";

            var file = File.With("DF796179-DB2F-4A06-B4D5-AD7F012CC2CC", "2021-08-09T18:15:02.4214747Z");

            var fileHash = "8n45KHxmXabrze7rq/s9Ww==";

            using var destinationStream = new System.IO.MemoryStream();

            var fileMetadata = new FileMetadata("title", "description", "group-name", file.Version, "owner", file.Name, ".extension", 396764, blobName, clock.UtcNow, fileHash, FileStatus.Verified);

            var fileWriteDetails = await fileRepository.WriteToStreamAsync(fileMetadata, destinationStream, cancellationToken);

            Assert.IsNotNull(fileWriteDetails);

            var fileBytes = destinationStream.ToArray();

            Assert.IsTrue(396764 == fileBytes.Length);
            Assert.IsTrue(396764 == fileWriteDetails.ContentLength);

            Assert.AreEqual(fileHash, fileWriteDetails.ContentHash);
        }

        [TestMethod]
        public async Task GetMetadataAsync_SanityCheckForLocalDevOnly()
        {
            var cancellationToken = new CancellationToken();

            var optionsAccessor = new Moq.Mock<IOptions<MemoryCacheOptions>>();

            optionsAccessor.Setup(x => x.Value).Returns(new MemoryCacheOptions());

            var memoryCache = new MemoryCache(optionsAccessor.Object);

            var logger = new Moq.Mock<ILogger<FileRepository>>().Object;

            var clock = new SystemClock();

            var configurationBuilder = new ConfigurationBuilder();

            // NB - Given the SUT is actually connecting to blob storage and a sql db, the connection strings etc are stored in a 
            //      local secrets file that is not included in source control.  If running these tests locally, ensure this file is 
            //      present in your project and that it contains the entries we need

            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

            var configuration = configurationBuilder.Build();

            var azurePlatformConfiguration = new AzurePlatformConfiguration()
            {
                AzureBlobStorage = new AzureBlobStorageConfiguration() { ContainerName = configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:ContainerName") }
            };

            var azurePlatformConfigurationOptionsSnapshot = new Moq.Mock<IOptionsSnapshot<AzurePlatformConfiguration>>();

            azurePlatformConfigurationOptionsSnapshot.Setup(x => x.Value).Returns(azurePlatformConfiguration);

            var primaryServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:PrimaryServiceUrl"), UriKind.Absolute);
            var geoRedundantServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:GeoRedundantServiceUrl"), UriKind.Absolute);

            var azureBlobStorageClient = new AzureBlobStoreClient(primaryServiceUrl, geoRedundantServiceUrl, memoryCache, clock, default);

            var readWriteConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadWriteConnectionString"); 
            var readOnlyConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadOnlyConnectionString"); 

            var sqlLogger = new Moq.Mock<ILogger<AzureSqlClient>>().Object;

            var sqlCnFactoryLogger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var sqlDbConnectionFactory = new AzureSqlDbConnectionFactory(readWriteConnectionString, readOnlyConnectionString, sqlCnFactoryLogger);

            var azureSqlClient = new AzureSqlClient(sqlDbConnectionFactory, sqlLogger);

            IFileRepository fileRepository = new FileRepository(azureBlobStorageClient, azureSqlClient, azurePlatformConfigurationOptionsSnapshot.Object, logger);

            var file = File.With("DF796179-DB2F-4A06-B4D5-AD7F012CC2CC", "2021-08-09T18:15:02.4214747Z");

            var fileMetadata = await fileRepository.GetMetadataAsync(file, cancellationToken);

            Assert.IsNotNull(fileMetadata);

            Assert.IsFalse(fileMetadata.IsEmpty);

            Assert.AreEqual(file.Version, fileMetadata.Version);
        }

        [TestMethod]
        public async Task GenerateEphemeralDownloadLink_SanityCheckForLocalDevOnly()
        {
            var cancellationToken = new CancellationToken();

            var optionsAccessor = new Moq.Mock<IOptions<MemoryCacheOptions>>();

            optionsAccessor.Setup(x => x.Value).Returns(new MemoryCacheOptions());

            var memoryCache = new MemoryCache(optionsAccessor.Object);

            var logger = new Moq.Mock<ILogger<FileRepository>>().Object;

            var clock = new SystemClock();

            var configurationBuilder = new ConfigurationBuilder();

            // NB - Given the SUT is actually connecting to blob storage and a sql db, the connection strings etc are stored in a 
            //      local secrets file that is not included in source control.  If running these tests locally, ensure this file is 
            //      present in your project and that it contains the entries we need

            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

            var configuration = configurationBuilder.Build();

            var azurePlatformConfiguration = new AzurePlatformConfiguration()
            {
                AzureBlobStorage = new AzureBlobStorageConfiguration() { ContainerName = configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:ContainerName") }
            };

            var azurePlatformConfigurationOptionsSnapshot = new Moq.Mock<IOptionsSnapshot<AzurePlatformConfiguration>>();

            azurePlatformConfigurationOptionsSnapshot.Setup(x => x.Value).Returns(azurePlatformConfiguration);

            var primaryServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:PrimaryServiceUrl"), UriKind.Absolute);
            var geoRedundantServiceUrl = new Uri(configuration.GetValue<string>("AzurePlatform:AzureBlobStorage:GeoRedundantServiceUrl"), UriKind.Absolute);

            var azureBlobStorageClient = new AzureBlobStoreClient(primaryServiceUrl, geoRedundantServiceUrl, memoryCache, clock, default);

            var readWriteConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadWriteConnectionString");
            var readOnlyConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadOnlyConnectionString");

            var sqlLogger = new Moq.Mock<ILogger<AzureSqlClient>>().Object;

            var sqlCnFactoryLogger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var sqlDbConnectionFactory = new AzureSqlDbConnectionFactory(readWriteConnectionString, readOnlyConnectionString, sqlCnFactoryLogger);

            var azureSqlClient = new AzureSqlClient(sqlDbConnectionFactory, sqlLogger);

            IFileRepository fileRepository = new FileRepository(azureBlobStorageClient, azureSqlClient, azurePlatformConfigurationOptionsSnapshot.Object, logger);

            var file = File.With("DF796179-DB2F-4A06-B4D5-AD7F012CC2CC", "2021-08-09T18:15:02.4214747Z");

            var fileMetadata = await fileRepository.GetMetadataAsync(file, cancellationToken);

            var uri = await fileRepository.GeneratePublicEphemeralDownloadLink(fileMetadata, cancellationToken);

            Assert.IsNotNull(uri);

            Assert.IsTrue(uri.IsAbsoluteUri);
        }
#endif
    }
}