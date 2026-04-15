using FluentAssertions;
using SmAutoMapper.Compilation;

namespace MyAutoMapper.UnitTests.Compilation;

public class CollectionProjectionBuilderTests
{
    [Theory]
    [InlineData(typeof(List<int>), typeof(int))]
    [InlineData(typeof(IEnumerable<string>), typeof(string))]
    [InlineData(typeof(ICollection<int>), typeof(int))]
    [InlineData(typeof(IReadOnlyList<int>), typeof(int))]
    [InlineData(typeof(IReadOnlyCollection<int>), typeof(int))]
    [InlineData(typeof(IList<int>), typeof(int))]
    [InlineData(typeof(int[]), typeof(int))]
    public void TryGetElementType_supported_collections(Type input, Type expectedElement)
    {
        var ok = CollectionProjectionBuilder.TryGetElementType(input, out var element);
        ok.Should().BeTrue();
        element.Should().Be(expectedElement);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(Dictionary<int, int>))]
    [InlineData(typeof(SortedDictionary<int, int>))]
    [InlineData(typeof(IDictionary<int, int>))]
    [InlineData(typeof(int[,]))]
    public void TryGetElementType_rejects_non_collection(Type input)
    {
        var ok = CollectionProjectionBuilder.TryGetElementType(input, out _);
        ok.Should().BeFalse();
    }
}
