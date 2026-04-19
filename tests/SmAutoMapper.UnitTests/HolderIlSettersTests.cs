using System.Collections.Concurrent;
using FluentAssertions;
using SmAutoMapper.Parameters;
using Xunit;

namespace SmAutoMapper.UnitTests;

public sealed class HolderIlSettersTests
{
    private static HolderTypeInfo MakeHolder(params (string Name, Type Type)[] slots)
    {
        var factory = new ClosureHolderFactory();
        var parameterSlots = slots
            .Select(s =>
            {
                var slotType = typeof(ParameterSlot<>).MakeGenericType(s.Type);
                return (IParameterSlot)Activator.CreateInstance(slotType, s.Name)!;
            })
            .ToList();
        return factory.GetOrCreateHolderType(parameterSlots);
    }

    [Fact]
    public void Factory_CreatesNewInstanceEachCall()
    {
        var info = MakeHolder(("X", typeof(int)));

        var a = info.Factory();
        var b = info.Factory();

        a.Should().NotBeSameAs(b);
        a.Should().BeOfType(info.HolderType);
    }

    [Fact]
    public void Setters_AssignsReferenceType()
    {
        var info = MakeHolder(("Name", typeof(string)));
        var holder = info.Factory();

        info.Setters["Name"](holder, "hello");

        info.PropertyMap["Name"].GetValue(holder).Should().Be("hello");
    }

    [Fact]
    public void Setters_AssignsValueType()
    {
        var info = MakeHolder(("Count", typeof(int)), ("Date", typeof(DateTime)));
        var holder = info.Factory();

        info.Setters["Count"](holder, 42);
        info.Setters["Date"](holder, new DateTime(2026, 4, 17));

        info.PropertyMap["Count"].GetValue(holder).Should().Be(42);
        info.PropertyMap["Date"].GetValue(holder).Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void Setters_AssignsNullableValueType_NullAndValue()
    {
        var info = MakeHolder(("Maybe", typeof(int?)));
        var holder = info.Factory();

        info.Setters["Maybe"](holder, null);
        info.PropertyMap["Maybe"].GetValue(holder).Should().BeNull();

        info.Setters["Maybe"](holder, 7);
        info.PropertyMap["Maybe"].GetValue(holder).Should().Be(7);
    }

    [Fact]
    public void Setters_IndependentBetweenProperties()
    {
        var info = MakeHolder(("A", typeof(int)), ("B", typeof(int)));
        var holder = info.Factory();

        info.Setters["A"](holder, 1);
        info.Setters["B"](holder, 2);

        info.PropertyMap["A"].GetValue(holder).Should().Be(1);
        info.PropertyMap["B"].GetValue(holder).Should().Be(2);
    }

    [Fact]
    public void Setters_WrongType_ThrowsInvalidCastException()
    {
        var info = MakeHolder(("N", typeof(int)));
        var holder = info.Factory();

        var act = () => info.Setters["N"](holder, "not-an-int");

        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void Factory_ParallelCreation_ProducesDistinctInstances()
    {
        var info = MakeHolder(("X", typeof(int)));
        var bag = new ConcurrentBag<object>();

        Parallel.For(0, 100, _ => bag.Add(info.Factory()));

        bag.Should().HaveCount(100);
        bag.Distinct().Should().HaveCount(100);
    }
}
