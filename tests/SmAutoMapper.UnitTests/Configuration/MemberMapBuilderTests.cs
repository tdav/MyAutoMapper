using System.Linq.Expressions;
using FluentAssertions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.UnitTests.Configuration;

public class MemberMapBuilderTests
{
    private sealed class Src { public List<int> Items { get; set; } = new(); }
    private sealed class Dst { public List<string> Items { get; set; } = new(); }

    private static MemberMapBuilder<Src, Dst, List<string>> NewBuilder() =>
        (MemberMapBuilder<Src, Dst, List<string>>)Activator.CreateInstance(
            typeof(MemberMapBuilder<,,>).MakeGenericType(typeof(Src), typeof(Dst), typeof(List<string>)),
            nonPublic: true)!;

    [Fact]
    public void MapFrom_with_different_member_type_stores_expression()
    {
        var builder = NewBuilder();
        Expression<Func<Src, List<int>>> expr = s => s.Items;

        builder.MapFrom(expr);

        builder.SourceExpression.Should().BeSameAs(expr);
        builder.IsIgnored.Should().BeFalse();
        builder.HasParameterizedSource.Should().BeFalse();
    }

    [Fact]
    public void Parameterised_MapFrom_with_different_member_type_stores_all_state()
    {
        var builder = NewBuilder();
        var slot = new ParameterSlot<string>("lang");
        Expression<Func<Src, string, List<int>>> expr = (s, _) => s.Items;

        builder.MapFrom(slot, expr);

        builder.HasParameterizedSource.Should().BeTrue();
        builder.ParameterSlot.Should().BeSameAs(slot);
        builder.ParameterizedSourceExpression.Should().BeSameAs(expr);
    }
}
