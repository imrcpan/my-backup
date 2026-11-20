using Xunit;

namespace Backup.Core.Tests;

public class RetentionPolicyTests
{
    [Fact]
    public void A_policy_that_keeps_something_is_valid()
    {
        RetentionPolicy policy = new(KeepLast: 3, KeepMonthly: 12);
        Assert.Same(policy, policy.Validated());
        Assert.Equal(1, new RetentionPolicy(KeepYearly: 1).Validated().KeepYearly);
    }

    [Fact]
    public void A_policy_that_keeps_nothing_is_refused()
    {
        Assert.Equal("retention_empty", Assert.Throws<DomainException>(() => new RetentionPolicy().Validated()).Code);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -5, 1)]
    [InlineData(100_001, 0, 0, 0, 0)]
    public void Out_of_range_numbers_are_refused(int last, int daily, int weekly, int monthly, int yearly)
    {
        Assert.Equal("retention_invalid", Assert.Throws<DomainException>(() => new RetentionPolicy(last, daily, weekly, monthly, yearly).Validated()).Code);
    }
}
