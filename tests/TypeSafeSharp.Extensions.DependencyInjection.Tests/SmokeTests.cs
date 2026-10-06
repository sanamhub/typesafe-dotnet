namespace TypeSafeSharp.Extensions.DependencyInjection.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Assembly_IsStrongNamed()
        => Assert.NotEmpty(typeof(TypeSafeClientOptions).Assembly.GetName().GetPublicKeyToken()!);
}
