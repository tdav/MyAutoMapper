namespace SmAutoMapper.Internal;

internal static class AotMessages
{
    public const string DynamicCode =
        "SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.";

    public const string UnreferencedCode =
        "SmAutoMapper uses reflection over mapped types; members may be trimmed.";
}
