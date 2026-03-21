namespace MyAutoMapper.Validation;

public sealed class MappingValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public MappingValidationException(IReadOnlyList<string> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

    private static string FormatMessage(IReadOnlyList<string> errors)
    {
        return $"Mapping configuration validation failed with {errors.Count} error(s):{Environment.NewLine}" +
               string.Join(Environment.NewLine, errors.Select((e, i) => $"  {i + 1}. {e}"));
    }
}
