using FluentAssertions;
using Microsoft.Extensions.Logging;
using MyAutoMapper.Configuration;
using MyAutoMapper.Validation;

namespace MyAutoMapper.UnitTests.Validation;

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
