using Deleguard.Api.Http;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Ide;

public sealed class InstallerModel : PageModel
{
    public IActionResult OnGet(string platform)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        Response.Headers.CacheControl="no-store";
        var release=PortableReleases.Find(HttpContext,platform);
        if(release is null)return NotFound();
        var path=PortableReleases.VerifiedPath(HttpContext,release);
        return path is null?NotFound():PhysicalFile(path,"application/zip",release.File);
    }
}
