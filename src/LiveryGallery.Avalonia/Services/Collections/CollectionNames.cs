namespace LiveryGallery.Services.Collections;

/// <summary>
/// The names the user gives to what holds liveries or screenshots (the albums of screenshots, the folders of the
/// storage): spaces trimmed, 1 to <see cref="MaxLength"/> characters, no control characters, and not the name of
/// another one, whatever the case. Pure
/// </summary>
internal static class CollectionNames
{
    public const int MaxLength = 64;

    /// <summary>The name as it is kept; null when it cannot be one (empty, too long, a control character)</summary>
    public static string? Clean(string? name)
    {
        if (name is null) return null;
        string trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxLength || trimmed.Any(char.IsControl)) return null;
        return trimmed;
    }

    /// <summary>Whether <paramref name="name"/> is already taken by one of <paramref name="names"/> (not counting <paramref name="except"/>'s own)</summary>
    public static bool IsTaken(string name, IEnumerable<(string Id, string Name)> names, string? except = null) =>
        names.Any(n => n.Id != except && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
}
