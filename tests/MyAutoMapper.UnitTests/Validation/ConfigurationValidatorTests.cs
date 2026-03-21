using FluentAssertions;
using MyAutoMapper.Configuration;
using MyAutoMapper.Validation;

namespace MyAutoMapper.UnitTests.Validation;

public class ConfigurationValidatorTests
{
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
}
