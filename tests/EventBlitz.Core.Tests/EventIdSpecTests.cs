using EventBlitz.Core.Models;
using Xunit;

namespace EventBlitz.Core.Tests;

public class EventIdSpecTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_is_the_empty_spec(string? text)
    {
        Assert.True(EventIdSpec.TryParse(text, out var spec, out _));
        Assert.True(spec!.IsEmpty);
        Assert.True(spec.Matches(12345));
        Assert.Null(spec.ToXPath());
    }

    [Fact]
    public void Single_ids_ranges_and_exclusions_parse()
    {
        Assert.True(EventIdSpec.TryParse("1000, 2000-2010 !2005 -7", out var spec, out _));
        Assert.True(spec!.Matches(1000));
        Assert.True(spec.Matches(2003));
        Assert.False(spec.Matches(2005));
        Assert.False(spec.Matches(7));
        Assert.False(spec.Matches(3000));
        Assert.Equal("(EventID=1000 or (EventID>=2000 and EventID<=2010)) and not(EventID=2005) and not(EventID=7)", spec.ToXPath());
    }

    [Fact]
    public void Only_exclusions_let_everything_else_through()
    {
        Assert.True(EventIdSpec.TryParse("!4624", out var spec, out _));
        Assert.True(spec!.Matches(1));
        Assert.False(spec.Matches(4624));
        Assert.Equal("not(EventID=4624)", spec.ToXPath());
    }

    [Fact]
    public void Reversed_range_is_normalised()
    {
        Assert.True(EventIdSpec.TryParse("10-5", out var spec, out _));
        Assert.True(spec!.Matches(7));
        Assert.Equal("((EventID>=5 and EventID<=10))", spec.ToXPath());
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("1000, x-y", "x-y")]
    public void Garbage_reports_the_bad_token(string text, string expectedToken)
    {
        Assert.False(EventIdSpec.TryParse(text, out _, out var error));
        Assert.Equal(expectedToken, error);
    }
}
