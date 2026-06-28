using Vortex.Shared;

namespace Vortex.Tests;

public sealed class SharedContractTests
{
    [Fact]
    public void SecretMasker_DoesNotExposeFullSecret()
    {
        var masked = SecretMasker.Mask("sk-test-1234567890abcd");
        Assert.Contains("••••", masked);
        Assert.DoesNotContain("1234567890", masked);
    }

    [Theory]
    [InlineData("src/App.cs", true)]
    [InlineData("../secret.txt", false)]
    [InlineData("C:/Windows/win.ini", false)]
    public void PathSafety_RejectsTraversalAndRootedPaths(string path, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsSafeRelativePath(path));
    }
}
