using Zeus.Plugins.Contracts;

namespace Zeus.Plugins.Contracts.Tests;

public class AbiVersionTests
{
    [Fact]
    public void Current_IsPositiveInteger()
    {
        Assert.True(AbiVersion.Current >= 1);
    }

    [Fact]
    public void SdkVersion_ParsesAsSemVer()
    {
        Assert.True(Version.TryParse(AbiVersion.SdkVersion, out _));
    }

    [Fact]
    public void SdkVersion_MajorMatchesAbi()
    {
        // Bump major in lockstep with ABI per ADR §3.4.
        var sdkMajor = Version.Parse(AbiVersion.SdkVersion).Major;
        Assert.Equal(AbiVersion.Current, sdkMajor);
    }
}
