using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;

namespace XactJobs.UI
{
    public static class ApplicationBuilderExtensions
    {
        public static WebApplication UseXactJobsUI(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            var embeddedProvider = new EmbeddedFileProvider(
                Assembly.GetExecutingAssembly(),
                "XactJobs.UI.EmbeddedUI.browser" // Namespace + folder
            );

            var requestPath = Constants.RoutePrefix;

            // Serve default files like index.html, must be before UseStaticFiles
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = embeddedProvider,
                RequestPath = requestPath,
                DefaultFileNames = ["index.html"]
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = embeddedProvider,
                RequestPath = requestPath,
                RedirectToAppendTrailingSlash = true,
            });

            return app;
        }
    }
}