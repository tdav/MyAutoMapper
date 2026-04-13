namespace SmAutoMapper.Runtime;

public static class ProjectionProviderAccessor
{
    private static IProjectionProvider? _instance;

    internal static void SetInstance(IProjectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _instance = provider;
    }

    public static IProjectionProvider Instance =>
        _instance ?? throw new InvalidOperationException(
            "IProjectionProvider is not configured. Call services.AddMapping() first.");
}
