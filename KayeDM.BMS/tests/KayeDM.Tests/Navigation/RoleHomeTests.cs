using FluentAssertions;
using KayeDM.Application.Navigation;

namespace KayeDM.Tests.Navigation;

public class RoleHomeTests
{
    [Fact]
    public void Resolve_OwnerRole_ReturnsDashboard()
    {
        RoleHome.Resolve(new[] { "Owner" }).Should().Be("/dashboard");
    }

    [Fact]
    public void Resolve_CashierRole_ReturnsPos()
    {
        RoleHome.Resolve(new[] { "Cashier" }).Should().Be("/pos");
    }

    [Fact]
    public void Resolve_OwnerAndCashier_PrefersOwner()
    {
        RoleHome.Resolve(new[] { "Cashier", "Owner" }).Should().Be("/dashboard");
    }

    [Fact]
    public void Resolve_NoRoles_DefaultsToPos()
    {
        RoleHome.Resolve(Array.Empty<string>()).Should().Be("/pos");
    }
}
