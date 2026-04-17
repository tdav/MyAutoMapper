namespace SmAutoMapper.Runtime;

[Obsolete("Inject IProjectionProvider via DI and use the ProjectTo(IQueryable, IProjectionProvider) overload. " +
          "Will be removed in 2.0.", DiagnosticId = "SMAM0001")]
public static class ProjectionProviderAccessor
{
    private static volatile IProjectionProvider? _instance;

    internal static void SetInstance(IProjectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _instance = provider;
    }

    public static IProjectionProvider Instance =>
        _instance ?? throw new InvalidOperationException(
            "IProjectionProvider is not configured. Call services.AddMapping() first.");
}
