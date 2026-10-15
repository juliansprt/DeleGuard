using Deleguard.Application;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;

namespace Deleguard.UnitTests;

public sealed class ApplicationAdminHandlerTests
{
    [Fact]
    public void Rejects_reserved_administration_slug()
    {
        var ex = Assert.Throws<RequestValidationException>(() => ApplicationAdminHandler.NormalizeSlug("administration"));
        Assert.Contains("reserved", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizes_product_slug()
    {
        Assert.Equal("billing", ApplicationAdminHandler.NormalizeSlug("Billing"));
    }

    [Fact]
    public void Application_name_bounds()
    {
        Assert.Throws<RequestValidationException>(() => ApplicationAdminHandler.NormalizeName(""));
        Assert.Equal("Invoices", ApplicationAdminHandler.NormalizeName("  Invoices  "));
    }
}

public sealed class PolicyCatalogGuardTests
{
    [Fact]
    public void Administration_catalog_cannot_be_deleted_by_slug_or_action()
    {
        Assert.True(Deleguard.Application.Policies.PolicyAdminHandler.IsAdministrationCatalog("admin-application-create", "admin.application.create"));
        Assert.True(Deleguard.Application.Policies.PolicyAdminHandler.IsAdministrationCatalog("invoice-cancel", "admin.user.list"));
        Assert.False(Deleguard.Application.Policies.PolicyAdminHandler.IsAdministrationCatalog("invoice-cancel", "invoice.cancel"));
    }
}
