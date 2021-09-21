using FutureNHS.WOPIHost.PlatformHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data.SqlClient;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS_WOPI_Host_UnitTests.PlatformHelpers
{
    [TestClass]
    public sealed class AzureSqlDbConnectionFactoryTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task GetReadOnlyConnectionAsync_ThrowsIfInvalidStructureForConnectionString()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory("read-write", "read-only", logger);

            _ = await azureSqlDbConnectionFactory.GetReadOnlyConnectionAsync(cancellationToken);
        }

        [TestMethod]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task GetReadOnlyConnectionAsync_ThrowsIfCancelled()
        {
            var cts = new CancellationTokenSource();

            var cancellationToken = cts.Token;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            cts.Cancel();

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory("read-write", "read-only", logger);

            _ = await azureSqlDbConnectionFactory.GetReadOnlyConnectionAsync(cancellationToken);
        }

        [TestMethod]
        public async Task GetReadOnlyConnectionAsync_DoesNotThrowIfInvalidConnectionString_ThrowsOnOpen()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var INVALID_READONLY_CONNECTIONSTRING = "Server=tcp:" + Guid.NewGuid().ToString() + ".database.windows.net,1433;Initial Catalog=initial-catalog;Persist Security Info=False;User ID=my-user-id;Password=my-password;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;ApplicationIntent=ReadOnly";

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory("read-write", INVALID_READONLY_CONNECTIONSTRING, logger);

            _ = await azureSqlDbConnectionFactory.GetReadOnlyConnectionAsync(cancellationToken);
        }

#if DEBUG
        [TestMethod]
        public async Task GetReadOnlyConnectionAsync_ReturnsClosedConnection()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

            var configuration = configurationBuilder.Build();

            var readWriteConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadWriteConnectionString");
            var readOnlyConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadOnlyConnectionString");

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory(readOnlyConnectionString, readWriteConnectionString, logger);

            using var connection = await azureSqlDbConnectionFactory.GetReadOnlyConnectionAsync(cancellationToken);

            Assert.IsNotNull(connection);
            Assert.IsInstanceOfType(connection, typeof(SqlConnection));

            //Assert.AreEqual(readOnlyConnectionString, connection.ConnectionString); // Can't test this as it rewrites the connection string (application intent is lost)
            Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        }
#endif



        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task GetReadWriteConnectionAsync_ThrowsIfInvalidStructureForConnectionString()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory("read-write", "read-only", logger);

            _ = await azureSqlDbConnectionFactory.GetReadWriteConnectionAsync(cancellationToken);
        }

        [TestMethod]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task GetReadWriteConnectionAsync_ThrowsIfCancelled()
        {
            var cts = new CancellationTokenSource();

            var cancellationToken = cts.Token;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            cts.Cancel();

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory("read-write", "read-only", logger);

            _ = await azureSqlDbConnectionFactory.GetReadWriteConnectionAsync(cancellationToken);
        }

        [TestMethod]
        public async Task GetReadWriteConnectionAsync_DoesNotThrowIfInvalidConnectionString_ThrowsOnOpen()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var INVALID_READWRITE_CONNECTIONSTRING = "Server=tcp:" + Guid.NewGuid().ToString() + ".database.windows.net,1433;Initial Catalog=initial-catalog;Persist Security Info=False;User ID=my-user-id;Password=my-password;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory(INVALID_READWRITE_CONNECTIONSTRING, "read-only", logger);

            _ = await azureSqlDbConnectionFactory.GetReadWriteConnectionAsync(cancellationToken);
        }

#if DEBUG
        [TestMethod]
        public async Task GetReadWriteConnectionAsync_ReturnsClosedConnection()
        {
            var cancellationToken = CancellationToken.None;

            var logger = new Moq.Mock<ILogger<AzureSqlDbConnectionFactory>>().Object;

            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

            var configuration = configurationBuilder.Build();

            var readWriteConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadWriteConnectionString");
            var readOnlyConnectionString = configuration.GetValue<string>("AzurePlatform:AzureSql:ReadOnlyConnectionString");

            IAzureSqlDbConnectionFactory azureSqlDbConnectionFactory = new AzureSqlDbConnectionFactory(readOnlyConnectionString, readWriteConnectionString, logger);

            using var connection = await azureSqlDbConnectionFactory.GetReadWriteConnectionAsync(cancellationToken);

            Assert.IsNotNull(connection);
            Assert.IsInstanceOfType(connection, typeof(SqlConnection));

            //Assert.AreEqual(readWriteConnectionString, connection.ConnectionString); // Can't test this as it rewrites the connection string (application intent is lost)
            Assert.AreEqual(System.Data.ConnectionState.Closed, connection.State);
        }
#endif
    }
}
