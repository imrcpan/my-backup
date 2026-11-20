using Xunit;

namespace Backup.Core.Tests;

public class StoragePathTests
{
    [Theory]
    [InlineData("/path/to/some", "/path/to/some")]
    [InlineData("/path/to/some/", "/path/to/some")]
    [InlineData("/path/to/some///", "/path/to/some")]
    [InlineData("//path//to/./some", "/path/to/some")]
    [InlineData("  /path/to/some/  ", "/path/to/some")]
    [InlineData("/", "/")]
    [InlineData("///", "/")]
    public void Normalize_gives_one_form(string input, string expected)
    {
        Assert.Equal(expected, StoragePath.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("/a/../b")]
    [InlineData("/a/..")]
    public void Normalize_rejects_invalid_paths(string? input)
    {
        DomainException error = Assert.Throws<DomainException>(() => StoragePath.Normalize(input));
        Assert.Equal("path_invalid", error.Code);
    }
}
