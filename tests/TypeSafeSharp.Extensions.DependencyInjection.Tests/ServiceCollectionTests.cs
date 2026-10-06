using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TypeSafeSharp.Extensions.DependencyInjection.Tests;

[Collection(EnvironmentGroup.Name)]
public sealed class ServiceCollectionTests : IDisposable
{
    private const string TestKey = "ts_test_0000000000000000";

    public ServiceCollectionTests() => EnvironmentFixture.Clear();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => EnvironmentFixture.Clear();

    // No defaults: no appsettings.json, no environment variables, nothing from the machine.
    private static HostApplicationBuilder Builder(Dictionary<string, string?>? settings = null)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        return builder;
    }

    [Fact]
    public void Configuration_BindsEveryOption()
    {
        var builder = Builder(new()
        {
            ["TypeSafe:ApiKey"] = TestKey,
            ["TypeSafe:BaseUrl"] = "https://gateway.example/api",
            ["TypeSafe:DefaultModel"] = "jev-1.13.0",
            ["TypeSafe:AttemptTimeout"] = "00:00:20",
            ["TypeSafe:TotalTimeout"] = "00:01:00",
            ["TypeSafe:Retry:MaxRetries"] = "5",
            ["TypeSafe:Retry:RetryOnTimeout"] = "false",
        });
        builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptionsMonitor<TypeSafeClientOptions>>().Get("TypeSafeSharp");

        Assert.Equal(TestKey, options.ApiKey);
        Assert.Equal(new Uri("https://gateway.example/api"), options.BaseUrl);
        Assert.Equal("jev-1.13.0", options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(20), options.AttemptTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), options.TotalTimeout);
        Assert.Equal(5, options.Retry.MaxRetries);
        Assert.False(options.Retry.RetryOnTimeout);
        Assert.Null(options.LoggerFactory);
    }

    [Fact]
    public async Task NoKeyAnywhere_FailsAtStartNotAtBuild()
    {
        // AC-3.8.
        var builder = Builder();
        builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));
        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(Ct));

        Assert.Contains("https://console.typesafe.ai/keys", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedKey_FailsAtStartWithoutEchoingIt()
    {
        var builder = Builder();
        builder.Services.AddTypeSafe(o => o.ApiKey = "ts_test_ZZZZ marker");
        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(Ct));

        Assert.DoesNotContain("ZZZZ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyOnlyInEnvironment_Starts()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", TestKey);
        var builder = Builder();
        builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));
        using var host = builder.Build();

        await host.StartAsync(Ct);
        await host.StopAsync(Ct);
    }

    [Fact]
    public void Client_IsASingleton()
    {
        var builder = Builder();
        builder.Services.AddTypeSafe(o => o.ApiKey = TestKey);
        using var host = builder.Build();

        Assert.Same(host.Services.GetRequiredService<TypeSafeClient>(), host.Services.GetRequiredService<TypeSafeClient>());
    }

    [Fact]
    public void NamedClient_DoesNotFollowRedirects()
    {
        var builder = Builder();
        builder.Services.AddTypeSafe(o => o.ApiKey = TestKey);
        using var host = builder.Build();

        HttpMessageHandler handler = host.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("TypeSafeSharp");
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.Equal(false, handler.GetType().GetProperty("AllowAutoRedirect")!.GetValue(handler));
    }

    [Fact]
    public async Task ReturnedBuilder_ConfiguresTheClientsHttpClient()
    {
        using var stub = new OkHandler();
        var builder = Builder();
        builder.Services
            .AddTypeSafe(o => { o.ApiKey = TestKey; o.BaseUrl = new Uri("https://api.typesafe.ai"); o.DefaultModel = "jev-latest"; })
            .ConfigurePrimaryHttpMessageHandler(() => stub);
        using var host = builder.Build();
        var client = host.Services.GetRequiredService<TypeSafeClient>();

        var response = await client.SystemOneAsync("text", new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("x") }, Ct);

        Assert.Equal(0.95, response.GetNoul("is_urgent").Noul);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", stub.LastUri!.AbsoluteUri);
    }

    [Fact]
    public void Arguments_AreChecked()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddTypeSafe((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddTypeSafe((Action<TypeSafeClientOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddTypeSafe(o => { }));
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            const string Body = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":1,"output_tokens":1}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
