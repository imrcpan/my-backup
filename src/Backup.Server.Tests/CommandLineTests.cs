using Backup.Server;
using Xunit;

namespace Backup.Server.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData(new[] { "-p", "1234" }, new[] { "--Port=1234" })]
    [InlineData(new[] { "-p=1234" }, new[] { "--Port=1234" })]
    [InlineData(new[] { "--port", "1234" }, new[] { "--Port=1234" })]
    [InlineData(new[] { "--port=1234" }, new[] { "--Port=1234" })]
    [InlineData(new[] { "--PORT", "1234" }, new[] { "--Port=1234" })]
    [InlineData(new[] { "--DataDir=/x", "-p", "1234", "--RclonePath", "/r" }, new[] { "--DataDir=/x", "--Port=1234", "--RclonePath", "/r" })]
    [InlineData(new string[0], new string[0])]
    [InlineData(new[] { "--Port=99" }, new[] { "--Port=99" })]
    [InlineData(new[] { "-p" }, new[] { "--Port=" })]
    public void The_port_options_become_one_form_and_other_arguments_stay(string[] args, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Normalize(args));
    }

    [Theory]
    [InlineData("1580", true, 1580)]
    [InlineData("1", true, 1)]
    [InlineData("65535", true, 65535)]
    [InlineData(null, true, 1580)]
    [InlineData("0", false, 0)]
    [InlineData("65536", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    public void A_port_is_a_number_from_1_to_65535_and_the_default_applies_only_if_none_was_given(string? setting, bool valid, int expected)
    {
        Assert.Equal(valid, CommandLine.TryGetPort(setting, 1580, out int port, out string? error));
        Assert.Equal(valid ? expected : 0, valid ? port : 0);
        Assert.Equal(!valid, error != null);
        if (!valid)
        {
            Assert.Contains("is not a port number", error);
        }
    }
}
