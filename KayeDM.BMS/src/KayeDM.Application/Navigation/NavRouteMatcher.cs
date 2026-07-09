namespace KayeDM.Application.Navigation;

// Every route in this app is a flat, fixed page (no dynamic per-record
// children), and nav groups no longer align with URL segments one-to-one —
// e.g. "/expenses/report" lives in the Reports group, not Expenses. So this
// matches full routes exactly; callers list every route a group owns rather
// than relying on a single segment to imply its children.
public static class NavRouteMatcher
{
    public static bool IsActive(string currentPath, IEnumerable<string> routes)
    {
        var normalizedPath = Normalize(currentPath);
        return routes.Any(route => normalizedPath == Normalize(route));
    }

    private static string Normalize(string path) => path.Trim('/').ToLowerInvariant();
}
