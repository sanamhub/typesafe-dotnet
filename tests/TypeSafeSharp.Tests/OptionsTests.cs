namespace TypeSafeSharp.Tests;

[Collection(EnvironmentGroup.Name)]
public sealed class OptionsTests : IDisposable
{
    private const string TestKey = "ts_test_0000000000000000";
    private const string MarkerKey = "ts_test_ZZZZ_marker";

    public OptionsTests() => EnvironmentFixture.Clear();

    public void Dispose() => EnvironmentFixture.Clear();

    private static TypeSafeClientOptions Options() => new() { ApiKey = TestKey };

    private static TypeSafeConfigurationException ConfigError(TypeSafeClientOptions options)
        => Assert.Throws<TypeSafeConfigurationException>(() => ClientSettings.From(options));

    [Fact]
    public void Defaults_WhenNothingIsSet()
    {
        var settings = ClientSettings.From(Options());

        Assert.Equal("https://api.typesafe.ai/", settings.BaseUrl.AbsoluteUri);
        Assert.Equal("jev-latest", settings.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.AttemptTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.TotalTimeout);
        Assert.Equal(2, settings.Retry.MaxRetries);
        Assert.True(settings.Retry.RetryOnTimeout);
        Assert.NotNull(settings.LoggerFactory);
    }

    [Fact]
    public void Environment_BeatsDefault()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "https://gateway.example/api/");
        Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", " jev-1.13.0 ");

        var settings = ClientSettings.From(Options());

        Assert.Equal("https://gateway.example/api", settings.BaseUrl.AbsoluteUri);
        Assert.Equal("jev-1.13.0", settings.DefaultModel);
    }

    [Fact]
    public void Code_BeatsEnvironment()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "https://env.example");
        Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "from-env");

        var options = Options();
        options.BaseUrl = new Uri("https://code.example");
        options.DefaultModel = "from-code";
        var settings = ClientSettings.From(options);

        Assert.Equal("https://code.example/", settings.BaseUrl.AbsoluteUri);
        Assert.Equal("from-code", settings.DefaultModel);
    }

    [Fact]
    public void WhitespaceEnvironmentValues_AreIgnored()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "   ");
        Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "\t");

        var settings = ClientSettings.From(Options());

        Assert.Equal("https://api.typesafe.ai/", settings.BaseUrl.AbsoluteUri);
        Assert.Equal("jev-latest", settings.DefaultModel);
    }

    [Fact]
    public void BlankDefaultModelInCode_FallsBack()
    {
        var options = Options();
        options.DefaultModel = " ";

        Assert.Equal("jev-latest", ClientSettings.From(options).DefaultModel);
    }

    [Fact]
    public void Key_FromEnvironment_IsTrimmed()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "  " + TestKey + "\n");

        Assert.True(ClientSettings.From(new TypeSafeClientOptions()).ApiKey == TestKey);
    }

    [Fact]
    public void Key_Missing_PointsToConsole()
    {
        var ex = ConfigError(new TypeSafeClientOptions());

        Assert.EndsWith("Create a key at https://console.typesafe.ai/keys.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_ExplicitEmpty_DoesNotFallBackToEnvironment()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", TestKey);

        Assert.Equal("The API key is empty.", ConfigError(new TypeSafeClientOptions { ApiKey = "  " }).Message);
    }

    [Theory]
    [InlineData("ts_test_ZZZZ marker", "whitespace")]
    [InlineData("ts_test_ZZZZ\nmarker", "whitespace")]
    [InlineData("ts_test_ZZZZ\u0001marker", "control")]
    [InlineData("ts_test_ZZZZ“marker", "non-ASCII")]
    [InlineData("ts_test_ZZZZémarker", "non-ASCII")]
    public void Key_BadCharacters_NameTheRuleNotTheKey(string key, string rule)
    {
        var ex = ConfigError(new TypeSafeClientOptions { ApiKey = key });

        Assert.Contains(rule, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ZZZZ", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com/?token=secret")]
    [InlineData("https://example.com/#secret")]
    public void BaseUrl_Rejected_WithoutEchoingIt(string address)
    {
        var options = Options();
        options.BaseUrl = new Uri(address);

        var ex = ConfigError(options);

        Assert.DoesNotContain("example.com", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrl_Relative_IsRejected()
    {
        var options = Options();
        options.BaseUrl = new Uri("/api", UriKind.Relative);

        ConfigError(options);
    }

    [Fact]
    public void BaseUrl_FromEnvironment_NotAUrl_IsRejected()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "api.typesafe.ai");

        Assert.Equal("TYPESAFE_BASE_URL is not an absolute URL.", ConfigError(Options()).Message);
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080/")]
    [InlineData("http://[::1]:8080", "http://[::1]:8080/")]
    [InlineData("https://gateway.example/api//", "https://gateway.example/api")]
    public void BaseUrl_Accepted(string address, string expected)
    {
        var options = Options();
        options.BaseUrl = new Uri(address);

        Assert.Equal(expected, ClientSettings.From(options).BaseUrl.AbsoluteUri);
    }

    public static TheoryData<TimeSpan> BadTimeouts => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        Timeout.InfiniteTimeSpan,
        TimeSpan.MaxValue,
    };

    [Theory]
    [MemberData(nameof(BadTimeouts))]
    public void Timeouts_OutOfRange_Throw(TimeSpan value)
    {
        var attempt = Options();
        attempt.AttemptTimeout = value;
        var total = Options();
        total.TotalTimeout = value;

        Assert.Equal("AttemptTimeout", Assert.Throws<ArgumentOutOfRangeException>(() => ClientSettings.From(attempt)).ParamName);
        Assert.Equal("TotalTimeout", Assert.Throws<ArgumentOutOfRangeException>(() => ClientSettings.From(total)).ParamName);
    }

    [Fact]
    public void NegativeMaxRetries_Throws()
    {
        var options = Options();
        options.Retry = new RetryPolicy { MaxRetries = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => ClientSettings.From(options));
    }

    [Fact]
    public void NullRetry_UsesDefault()
    {
        var options = Options();
        options.Retry = null!;

        Assert.Equal(2, ClientSettings.From(options).Retry.MaxRetries);
    }

    [Fact]
    public void RetryPolicy_StaticsAreNewInstances()
    {
        Assert.NotSame(RetryPolicy.Default, RetryPolicy.Default);
        Assert.NotSame(RetryPolicy.None, RetryPolicy.None);
        Assert.Equal(0, RetryPolicy.None.MaxRetries);

        RetryPolicy.Default.MaxRetries = 9;
        Assert.Equal(2, RetryPolicy.Default.MaxRetries);
    }

    [Fact]
    public void ToString_MasksTheKey()
    {
        var options = new TypeSafeClientOptions { ApiKey = MarkerKey, BaseUrl = new Uri("https://api.typesafe.ai") };

        var text = options.ToString();

        Assert.DoesNotContain("ZZZZ", text, StringComparison.Ordinal);
        Assert.Equal("TypeSafeClientOptions { ApiKey = ***, BaseUrl = https://api.typesafe.ai/, DefaultModel = null }", text);
        Assert.Contains("ApiKey = null", new TypeSafeClientOptions().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_AreASnapshot()
    {
        // AC-3.10: From neither changes the options nor keeps reading them.
        var options = new TypeSafeClientOptions { ApiKey = TestKey, DefaultModel = "a", Retry = new RetryPolicy { MaxRetries = 1 } };

        var settings = ClientSettings.From(options);
        Assert.Equal("a", options.DefaultModel);
        Assert.Null(options.BaseUrl);
        Assert.Equal(1, options.Retry.MaxRetries);

        options.DefaultModel = "b";
        options.Retry.MaxRetries = 5;
        options.AttemptTimeout = TimeSpan.FromSeconds(1);

        Assert.Equal("a", settings.DefaultModel);
        Assert.Equal(1, settings.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.AttemptTimeout);
    }

    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(529, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(422, false)]
    [InlineData(600, false)]
    public void RetryableStatuses(int status, bool expected)
        => Assert.Equal(expected, RetrySettings.IsRetryableStatus(status));
}
