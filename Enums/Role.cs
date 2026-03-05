namespace NexusCoreDotNet.Enums;

public enum Role
{
    VIEWER = 1,
    ASSET_MANAGER = 2,
    ORG_MANAGER = 3,
    SUPERADMIN = 4
}

public static class RoleExtensions
{
    public static bool HasAtLeast(this Role userRole, Role required)
        => (int)userRole >= (int)required;

    public static string ToDisplayString(this Role role) => role switch
    {
        Role.SUPERADMIN => "Super Admin",
        Role.ORG_MANAGER => "Org Manager",
        Role.ASSET_MANAGER => "Asset Manager",
        Role.VIEWER => "Viewer",
        _ => role.ToString()
    };
}
