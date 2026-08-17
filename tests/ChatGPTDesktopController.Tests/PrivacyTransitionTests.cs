using ChatGPTDesktopController;
using Xunit;

namespace ChatGPTDesktopController.Tests;

public sealed class PrivacyTransitionTests
{
    [Fact]
    public void Exact_exclude_from_capture_value_is_verified()
    {
        Assert.True(PrivacyTransitionPolicy.IsVerified(true, PrivacyGuardService.WdaExcludeFromCapture));
        Assert.False(PrivacyTransitionPolicy.NeedsRepair(true, PrivacyGuardService.WdaExcludeFromCapture));
    }

    [Theory]
    [InlineData(0x00000000u)]
    [InlineData(0x00000001u)]
    [InlineData(0x00000010u)]
    [InlineData(0xFFFFFFFFu)]
    public void Any_readable_non_exclusion_value_requires_repair(uint affinity)
    {
        Assert.False(PrivacyTransitionPolicy.IsVerified(true, affinity));
        Assert.True(PrivacyTransitionPolicy.NeedsRepair(true, affinity));
    }

    [Fact]
    public void Unreadable_affinity_is_never_claimed_as_verified()
    {
        Assert.False(PrivacyTransitionPolicy.IsVerified(false, PrivacyGuardService.WdaExcludeFromCapture));
        Assert.True(PrivacyTransitionPolicy.NeedsRepair(false, PrivacyGuardService.WdaExcludeFromCapture));
    }
}
