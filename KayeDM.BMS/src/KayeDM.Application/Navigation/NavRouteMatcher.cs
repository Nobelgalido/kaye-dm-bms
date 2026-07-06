namespace KayeDM.Application.Navigation;

public static class NavRouteMatcher
{
    public static bool IsActive(string currentPath, IEnumerable<string> routePrefixes)
    {
        var normalizedPath = Normalize(currentPath);
        return routePrefixes.Any(prefix =>
        {
            var normalizedPrefix = Normalize(prefix);
            return normalizedPath == normalizedPrefix
                || normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string Normalize(string path) => path.Trim('/').ToLowerInvariant();
}
