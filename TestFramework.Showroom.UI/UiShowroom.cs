using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Container.Web;
using TestFramework.Container.Web.Sites;
using TestFramework.UI.Browser.Configuration;
using TestFramework.UI.Web;
using TestFramework.Web.Extensions;
using TestFramework.Web.Site;

namespace TestFramework.Showroom.UI;

// ══════════════════════════════════════════════════════════════════════════════
//  UI SYSTEMS DIVISION - FACILITY DECLARATION
//
//  One resource: a storefront. A static site, checked into the repository under
//  Web/StorefrontSite, shipped into a web-server container by the environment,
//  and driven by a real browser.
//
//  Note what is absent. No address - the container publishes one under the site's
//  identifier, and the browser steps resolve it from there. No selectors - the
//  tests speak in the words a customer sees. And no reference to the site's files
//  from the test assembly - the browser gets what the web server hands out, which
//  is the same deal a customer gets.
// ══════════════════════════════════════════════════════════════════════════════

internal static class UiShowroom
{
    // ─── The application under test ───────────────────────────────────────────
    // A directory with an index.html at its root, named relative to this file.
    // The framework ships it into a web-server container and publishes where it
    // ended up. Nothing checks that the directory is "current", because for a
    // checked-in site, the repository IS current.

    internal sealed class StorefrontSiteDefinition : DockerSiteDefinition
    {
        public override SiteIdentifier Identifier => "storefront";

        public override SiteSource Source => SiteSource.Directory(StorefrontDirectory());

        protected override void Configure(DockerSiteBuilder builder)
        {
        }
    }

    // ─── Configuration ────────────────────────────────────────────────────────
    // The browser entry carries everything EXCEPT an address: which engine, which
    // channel, headless, and the viewport. The address arrives at run time, from
    // the container, under the site's own identifier - which is why the entry and
    // the site share their name, and why nothing here will go stale.

    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
            .LoadWebConfig()
            // ^ Registers the stores the environment publishes into, the site's
            //   address among them. An empty shelf is still a shelf.
            .AddService((services, _) =>
            {
                services.AddSingleton(new UiConfigStore([new("storefront", BrowserEntry())]));
                services.AddUiWebBridge();
                // ^ The bridge is what lets a browser identifier be answered by
                //   the site store. Without it the entry above would need a
                //   BaseUrl, and a BaseUrl is exactly what we refuse to know.
            })
            .Build();

    internal static DockerWebEnvironment CreateEnvironment()
        => new DockerWebEnvironment().Include<StorefrontSiteDefinition>();

    private static WebAppConfig BrowserEntry()
    {
        // The gate guarantees the variable is set by the time any chapter runs.
        string requested = UiShowroomGate.RequestedBrowser!.ToLowerInvariant();

        return new WebAppConfig
        {
            Browser = requested is "firefox" ? "firefox" : requested is "webkit" or "safari" ? "webkit" : "chromium",
            Channel = requested is "msedge" or "edge" ? "msedge" : requested is "chrome" ? "chrome" : null,
            Headless = true,
            Device = "Desktop 1080p",
        };
    }

    private static string StorefrontDirectory([CallerFilePath] string? declaringFile = null)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(declaringFile)!, "..", "Web", "StorefrontSite"));
}
