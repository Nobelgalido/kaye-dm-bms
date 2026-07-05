namespace KayeDM.Application.Navigation;

public static class RoleHome
{
    public const string Owner = "/dashboard";
    public const string Cashier = "/pos";

    public static string Resolve(IEnumerable<string> roles)
    {
        var roleSet = roles as ICollection<string> ?? roles.ToList();
        return roleSet.Contains("Owner") ? Owner : Cashier;
    }
}
