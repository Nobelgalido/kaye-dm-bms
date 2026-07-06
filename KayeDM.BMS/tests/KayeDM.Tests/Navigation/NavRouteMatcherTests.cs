using FluentAssertions;
using KayeDM.Application.Navigation;

namespace KayeDM.Tests.Navigation;

public class NavRouteMatcherTests
{
    [Theory]
    [InlineData("/expenses/report", "expenses", true)]
    [InlineData("/expenses", "expenses", true)]
    [InlineData("expenses/categories", "expenses", true)]
    [InlineData("/pos", "expenses", false)]
    [InlineData("/expensesreport", "expenses", false)]
    public void IsActive_MatchesExactOrNestedPrefix(string currentPath, string prefix, bool expected)
    {
        NavRouteMatcher.IsActive(currentPath, new[] { prefix }).Should().Be(expected);
    }

    [Fact]
    public void IsActive_MatchesAnyOfMultiplePrefixes()
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
