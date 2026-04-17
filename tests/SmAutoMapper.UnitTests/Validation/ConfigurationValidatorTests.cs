using FluentAssertions;
using Microsoft.Extensions.Logging;
using SmAutoMapper.Validation;
using SmAutoMapper.Configuration;

namespace SmAutoMapper.UnitTests.Validation;

public class ConfigurationValidatorTests
{
    // Source missing "Extra" property that exists on destination
    private class SourceWithoutExtra
    {
        public int Id { get; set; }
    }

    private class DestWithExtra
    {
        public int Id { get; set; }
        public string Extra { get; set; } = "";
    }

    private class UnmappedProfile : MappingProfile
    {
        public UnmappedProfile()
        {
            CreateMap<SourceWithoutExtra, DestWithExtra>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id));
        }
    }

    private class ValidProfile : MappingProfile
    {
        public ValidProfile()
        {
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.Price, o => o.MapFrom(s => s.Price));
        }
    }

    [Fact]
    public void Build_ValidProfile_DoesNotThrow()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ValidProfile>();

        var act = () => builder.Build();
        act.Should().NotThrow();
    }

    [Fact]
    public void MappingValidationException_ContainsErrors()
    {
        var errors = new List<string> { "Error 1", "Error 2" };
        var ex = new MappingValidationException(errors);

        ex.Errors.Should().HaveCount(2);
        ex.Message.Should().Contain("Error 1");
        ex.Message.Should().Contain("Error 2");
    }

    [Fact]
    public void Validate_UnmappedProperty_DoesNotThrow_And_LogsWarning()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<UnmappedProfile>();
        var configuration = builder.Build();

        var logMessages = new List<string>();
        var logger = new FakeLogger<ConfigurationValidator>(logMessages);
        var validator = new ConfigurationValidator(logger);

        var act = () => validator.Validate(configuration.GetAllTypeMaps());
        act.Should().NotThrow();

        logMessages.Should().ContainSingle(m => m.Contains("Extra") && m.Contains("not mapped"));
    }

    [Fact]
    public void Validate_UnmappedProperty_WithoutLogger_DoesNotThrow()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<UnmappedProfile>();
        var configuration = builder.Build();

        var validator = new ConfigurationValidator();

        var act = () => validator.Validate(configuration.GetAllTypeMaps());
        act.Should().NotThrow();
    }

    // --- ReverseMap skipped-property models ---

    private class ReverseSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Computed { get; set; } = "";
        public string Parameterized { get; set; } = "";
    }

    private class ReverseDest
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Computed { get; set; } = "";
        public string Parameterized { get; set; } = "";
        public string Ignored { get; set; } = "";
    }

    private class ReverseMapWithSkipsProfile : MappingProfile
    {
        public ReverseMapWithSkipsProfile()
        {
            CreateMap<ReverseSource, ReverseDest>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.Computed, o => o.MapFrom(s => s.Name + " " + s.Description)) // computed
                .Ignore(d => d.Ignored) // ignored
                .ReverseMap();
        }
    }

    [Fact]
    public void ReverseMap_SkippedProperties_AreTracked()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ReverseMapWithSkipsProfile>();
        var configuration = builder.Build();

        var configs = configuration.GetAllTypeMapConfigurations();

        // The forward config (ReverseSource -> ReverseDest) should have skipped properties
        var forwardConfig = configs.FirstOrDefault(c =>
            c.SourceType == typeof(ReverseSource) && c.DestinationType == typeof(ReverseDest));

        forwardConfig.Should().NotBeNull();
        forwardConfig!.SkippedReverseProperties.Should().Contain(s => s.Contains("Computed") && s.Contains("computed"));
        forwardConfig.SkippedReverseProperties.Should().Contain(s => s.Contains("Ignored") && s.Contains("ignored"));
    }

    [Fact]
    public void Validate_ReverseMap_SkippedProperties_LogsWarning()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ReverseMapWithSkipsProfile>();
        var configuration = builder.Build();

        var logMessages = new List<string>();
        var logger = new FakeLogger<ConfigurationValidator>(logMessages);
        var validator = new ConfigurationValidator(logger);

        validator.Validate(configuration.GetAllTypeMaps(), configuration.GetAllTypeMapConfigurations());

        logMessages.Should().Contain(m => m.Contains("ReverseMap") && m.Contains("skipped"));
    }

    /// <summary>
    /// Minimal fake logger that captures log messages for assertions.
    /// </summary>
    private class FakeLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages;

        public FakeLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
