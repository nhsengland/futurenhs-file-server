using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FutureNHS.WOPIHost.Handlers
{
    internal sealed class PostFileWopiRequest
        : WopiRequest
    {
        private readonly string _fileId;

        private PostFileWopiRequest(string fileId, string accessToken)
            : base(accessToken, isWriteAccessRequired: true, demandsProof: true)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentNullException(nameof(fileId));

            _fileId = fileId;
        }

        internal static PostFileWopiRequest With(string fileId, string accessToken) => new PostFileWopiRequest(fileId, accessToken);

        protected override async Task HandleAsyncImpl(HttpContext context, CancellationToken cancellationToken)
        {
            // NB - WHEN WRITING TO AZURE BLOB ENSURE WE SET THE CONTENT DISPOSITION HEADER TO THE NAME OF THE FILE THAT WAS UPLOADED
            //      THINK ABOUT WHETHER A USER CAN CHANGE THE FILENAME AFTER IT HAS BEEN UPLOADED.   IF SO, MIGHT
            //      CAUSE US PROBLEMS UNLESS BLOB STORAGE LETS US CHANGE IT
            //      x-ms-blob-content-disposition : https://docs.microsoft.com/en-us/rest/api/storageservices/put-blob

            // POST /wopi/files/(file_id)/content 

            // TODO - This is where we would update the file in our storage repository
            //        taking care of permissions, locking and versioning along the way 

            var hostingEnv = context.RequestServices.GetRequiredService<IWebHostEnvironment>();

            var filePath = Path.Combine(hostingEnv.ContentRootPath, "Files", _fileId);

            if (!System.IO.File.Exists(filePath)) return;

            var pipeReader = context.Request.BodyReader;

            using var fileStrm = System.IO.File.OpenWrite(filePath + "." + DateTime.UtcNow.ToFileTime());

            await context.Response.StartAsync(cancellationToken);

            await pipeReader.CopyToAsync(fileStrm, cancellationToken); // BUG: Isn't writing the whole file (why?)
        }
    }
}
