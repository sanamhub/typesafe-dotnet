using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace TypeSafeSharp;

// Reads the response with JsonDocument. Each required field has one place that checks it, so a
// malformed body always names its JSON path, and no JsonException or FormatException escapes.
internal static class ResponseReader
{
    public static SystemOneResponse ReadSystemOne(byte[] body, string? requestId)
    {
        using var document = Parse(body);
        var root = document.RootElement;
        var model = RequiredString(root, "model", "$");
        var usage = Required(root, "usage", JsonValueKind.Object, "$");
        var tokens = new Usage(RequiredInt64(usage, "input_tokens", "$.usage"), RequiredInt64(usage, "output_tokens", "$.usage"));

        var answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
        foreach (var property in Required(root, "answers", JsonValueKind.Object, "$").EnumerateObject())
        {
            // Last one wins on a duplicate key, as JSON.parse does in the JS SDK.
            answers[property.Name] = ReadAnswer(property.Value, "$.answers." + property.Name);
        }

        return new SystemOneResponse(model, new ReadOnlyDictionary<string, Answer>(answers), tokens, requestId);
    }

    public static IReadOnlyList<ModelCard> ReadModels(byte[] body)
    {
        using var document = Parse(body);
        var models = new List<ModelCard>();
        var index = 0;
        foreach (var item in Required(document.RootElement, "models", JsonValueKind.Array, "$").EnumerateArray())
        {
            var path = "$.models[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            models.Add(new ModelCard(
                RequiredString(item, "name", path),
                RequiredString(item, "description", path),
                RequiredString(item, "release_date", path)));
            index++;
        }

        return models.AsReadOnly();
    }

    public static int LevelKey(string key, string path)
        => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? level
            : throw new TypeSafeResponseValidationException(path + "." + key, "level key is not a non-negative integer");

    private static JsonDocument Parse(byte[] body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // An empty body and a gateway's HTML error page both land here.
            throw new TypeSafeResponseValidationException("$", "the body is not JSON");
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            var kind = document.RootElement.ValueKind;
            document.Dispose();
            throw new TypeSafeResponseValidationException("$", $"expected Object, got {kind}");
        }

        return document;
    }

    private static Answer ReadAnswer(JsonElement answer, string path)
    {
        if (answer.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeResponseValidationException(path, $"expected Object, got {answer.ValueKind}");
        }

        var type = RequiredString(answer, "type", path);
        switch (type)
        {
            case "noul":
                return new NoulAnswer(RequiredDouble(answer, "noul", path));
            case "choice":
                return new ChoiceAnswer(
                    RequiredString(answer, "choice", path),
                    RequiredDouble(answer, "confidence", path),
                    StringProbabilities(Required(answer, "probabilities", JsonValueKind.Object, path), path + ".probabilities"));
            case "score":
                var probabilitiesPath = path + ".probabilities";
                var probabilities = LevelProbabilities(Required(answer, "probabilities", JsonValueKind.Object, path), probabilitiesPath);
                if (probabilities.Count == 0)
                {
                    throw new TypeSafeResponseValidationException(probabilitiesPath, "no levels");
                }

                return new ScoreAnswer(
                    RequiredDouble(answer, "score", path),
                    RequiredDouble(answer, "confidence", path),
                    Legend(Required(answer, "legend", JsonValueKind.Object, path), path + ".legend"),
                    probabilities);
            default:
                return new UnknownAnswer(type, answer.Clone());
        }
    }

    private static ReadOnlyDictionary<string, double> StringProbabilities(JsonElement map, string path)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in map.EnumerateObject())
        {
            result[property.Name] = Number(property.Value, path + "." + property.Name);
        }

        return new ReadOnlyDictionary<string, double>(result);
    }

    private static ReadOnlyDictionary<int, double> LevelProbabilities(JsonElement map, string path)
    {
        var result = new Dictionary<int, double>();
        foreach (var property in map.EnumerateObject())
        {
            result[LevelKey(property.Name, path)] = Number(property.Value, path + "." + property.Name);
        }

        return new ReadOnlyDictionary<int, double>(result);
    }

    private static ReadOnlyDictionary<int, JsonElement> Legend(JsonElement map, string path)
    {
        var result = new Dictionary<int, JsonElement>();
        foreach (var property in map.EnumerateObject())
        {
            // Cloned because the document is disposed when reading ends.
            result[LevelKey(property.Name, path)] = property.Value.Clone();
        }

        return new ReadOnlyDictionary<int, JsonElement>(result);
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            throw new TypeSafeResponseValidationException(path + "." + name, "missing");
        }

        return value.ValueKind == kind
            ? value
            : throw new TypeSafeResponseValidationException(path + "." + name, $"expected {kind}, got {value.ValueKind}");
    }

    private static string RequiredString(JsonElement parent, string name, string path)
        => Required(parent, name, JsonValueKind.String, path).GetString()!;

    private static double RequiredDouble(JsonElement parent, string name, string path)
        => Number(Required(parent, name, JsonValueKind.Number, path), path + "." + name);

    // TryGetInt64 rather than GetInt64, which throws FormatException on 1.5 or a value past long.
    private static long RequiredInt64(JsonElement parent, string name, string path)
        => Required(parent, name, JsonValueKind.Number, path).TryGetInt64(out var value)
            ? value
            : throw new TypeSafeResponseValidationException(path + "." + name, "expected an integer");

    private static double Number(JsonElement value, string path)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : throw new TypeSafeResponseValidationException(path, $"expected a finite Number, got {value.ValueKind}");
}
