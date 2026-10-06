using System;
using System.Diagnostics;
using System.Globalization;

namespace TypeSafeSharp;

// Tag names follow the OpenTelemetry GenAI conventions as of 2026-10-06
// (github.com/open-telemetry/semantic-conventions-genai). They are marked Development and move;
// note any rename in the changelog (ADR-0010). Never tag state, instructions, answers or headers.
internal static class Telemetry
{
    public const string Name = "TypeSafeSharp";

    public static readonly ActivitySource Source = new(Name, SdkInfo.Version);

    // Null when nobody listens, so callers skip every tag at no cost.
    public static Activity? StartSystemOne(string model, Uri baseUrl)
    {
        var activity = Source.StartActivity("systemone " + model, ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("gen_ai.provider.name", "typesafe");
        activity.SetTag("gen_ai.operation.name", "systemone");
        activity.SetTag("gen_ai.request.model", model);
        activity.SetTag("server.address", baseUrl.Host);
        activity.SetTag("server.port", baseUrl.Port);
        return activity;
    }

    public static void Succeeded(Activity? activity, SystemOneResponse response, int statusCode)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("gen_ai.response.model", response.Model);
        activity.SetTag("gen_ai.usage.input_tokens", response.Usage.InputTokens);
        activity.SetTag("gen_ai.usage.output_tokens", response.Usage.OutputTokens);
        activity.SetTag("http.response.status_code", statusCode);
        activity.SetTag("typesafe.request_id", response.RequestId);
    }

    public static void Failed(Activity? activity, Exception error)
    {
        if (activity is null)
        {
            return;
        }

        if (error is TypeSafeApiException api)
        {
            var status = (int)api.StatusCode;
            activity.SetTag("http.response.status_code", status);
            activity.SetTag("typesafe.request_id", api.RequestId);
            activity.SetTag("error.type", status.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            activity.SetTag("error.type", error.GetType().FullName);
        }

        activity.SetStatus(ActivityStatusCode.Error);
    }
}
