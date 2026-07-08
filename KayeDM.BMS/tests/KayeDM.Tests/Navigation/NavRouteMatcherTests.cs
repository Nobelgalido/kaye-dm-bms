using FluentAssertions;
using KayeDM.Application.Navigation;

namespace KayeDM.Tests.Navigation;

public class NavRouteMatcherTests
{
    [Theory]
    [InlineData("/expenses", "expenses", true)]
    [InlineData("/expenses/report", "expenses", false)]
    [InlineData("expenses/categories", "expenses", false)]
    [InlineData("/pos", "expenses", false)]
    [InlineData("/expensesreport", "expenses", false)]
    public void IsActive_MatchesExactRouteOnly(string currentPath, string route, bool expected)
    {
        NavRouteMatcher.IsActive(currentPath, new[] { route }).Should().Be(expected);
    }

    [Fact]
    public void IsActive_DoesNotMatchSiblingRouteMovedToAnotherGroup()
    {
        // expenses/report lives in the Reports group, not Expenses, even
        // though it's nested under the same URL segment.
        NavRouteMatcher.IsActive("/expenses/report", new[] { "expenses", "expenses/categories" }).Should().BeFalse();
    }

    [Fact]
    public void IsActive_MatchesAnyOfMultipleRoutes()
    {
        NavRouteMatcher.IsActive("/closing", new[] { "pos", "closing" }).Should().BeTrue();
    }

    [Fact]
    public void IsActive_CaseInsensitive()
    {
        NavRouteMatcher.IsActive("/POS", new[] { "pos" }).Should().BeTrue();
    }

    [Fact]
    public void IsActive_NoPrefixesMatches_ReturnsFalse()
    {
        NavRouteMatcher.IsActive("/menu", Array.Empty<string>()).Should().BeFalse();
    }
}
