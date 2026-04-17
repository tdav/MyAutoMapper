using System.Linq.Expressions;
using FluentAssertions;
using SmAutoMapper.Compilation;

namespace SmAutoMapper.UnitTests.Compilation;

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

    [Fact]
    public void BuildSelect_wraps_in_Select_and_ToList_when_dest_is_List()
    {
        var srcParam = Expression.Parameter(typeof(int[]), "arr");
        var elemParam = Expression.Parameter(typeof(int), "x");
        var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

        var expr = CollectionProjectionBuilder.BuildSelect(
            sourceCollection: srcParam,
            elementProjection: elemLambda,
            destType: typeof(List<int>));

        var lambda = Expression.Lambda<Func<int[], List<int>>>(expr, srcParam).Compile();
        lambda(new[] { 1, 2, 3 }).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void BuildSelect_returns_array_when_dest_is_array()
    {
        var srcParam = Expression.Parameter(typeof(int[]), "arr");
        var elemParam = Expression.Parameter(typeof(int), "x");
        var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

        var expr = CollectionProjectionBuilder.BuildSelect(srcParam, elemLambda, typeof(int[]));
        var lambda = Expression.Lambda<Func<int[], int[]>>(expr, srcParam).Compile();
        lambda(new[] { 10 }).Should().Equal(11);
    }

    [Fact]
    public void BuildSelect_returns_IEnumerable_unwrapped_when_dest_is_IEnumerable()
    {
        var srcParam = Expression.Parameter(typeof(int[]), "arr");
        var elemParam = Expression.Parameter(typeof(int), "x");
        var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

        var expr = CollectionProjectionBuilder.BuildSelect(srcParam, elemLambda, typeof(IEnumerable<int>));
        var lambda = Expression.Lambda<Func<int[], IEnumerable<int>>>(expr, srcParam).Compile();
        lambda(new[] { 5, 6 }).Should().Equal(6, 7);
    }

    [Fact]
    public void BuildSelect_uses_ToList_for_ICollection_dest()
    {
        var srcParam = Expression.Parameter(typeof(int[]), "arr");
        var elemParam = Expression.Parameter(typeof(int), "x");
        var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

        var expr = CollectionProjectionBuilder.BuildSelect(srcParam, elemLambda, typeof(ICollection<int>));
        var lambda = Expression.Lambda<Func<int[], ICollection<int>>>(expr, srcParam).Compile();
        lambda(new[] { 3, 4 }).Should().Equal(4, 5);
    }

    [Fact]
    public void BuildSelect_uses_ToList_for_IReadOnlyCollection_dest()
    {
        var srcParam = Expression.Parameter(typeof(int[]), "arr");
        var elemParam = Expression.Parameter(typeof(int), "x");
        var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

        var expr = CollectionProjectionBuilder.BuildSelect(srcParam, elemLambda, typeof(IReadOnlyCollection<int>));

        // Compile and verify the runtime type is List<int>
        var lambda = Expression.Lambda<Func<int[], IReadOnlyCollection<int>>>(expr, srcParam).Compile();
        var result = lambda(new[] { 1, 2 });
        result.Should().BeOfType<List<int>>();
        result.Should().Equal(2, 3);
    }
}
