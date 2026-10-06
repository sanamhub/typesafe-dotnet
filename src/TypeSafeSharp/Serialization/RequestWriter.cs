using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

// Writes caller nodes with WriteTo and never adds them to a new parent: a JsonNode can have one
// parent, and one Question is often shared by concurrent requests (the batch sample does this).
internal static class RequestWriter
{
    public static byte[] Write(SystemOneRequest request, string model)
    {
        var extraBody = request.ExtraBody;
        if (extraBody is not null)
        {
            foreach (var pair in extraBody)
            {
                if (pair.Key is "state" or "model" or "questions")
                {
                    throw new ArgumentException($"ExtraBody cannot set '{pair.Key}'; use the request property instead.", nameof(request));
                }
            }
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("state");
            request.State.WriteTo(writer);
            writer.WriteString("model", model);
            writer.WritePropertyName("questions");
            writer.WriteStartObject();
            foreach (var pair in request.Questions)
            {
                writer.WritePropertyName(pair.Key);
                WriteQuestion(writer, pair.Value);
            }

            writer.WriteEndObject();
            if (extraBody is not null)
            {
                foreach (var pair in extraBody)
                {
                    WriteOpenValue(writer, pair.Key, pair.Value);
                }
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteQuestion(Utf8JsonWriter writer, Question question)
    {
        if (question is RawQuestion raw)
        {
            raw.Json.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", question.Type);
        WriteOpenValue(writer, "instructions", question.Instructions);
        switch (question)
        {
            // Both keys or neither: the API reference shows criteria for a Noul as a pair.
            case NoulQuestion noul when noul.WhenTrue is not null || noul.WhenFalse is not null:
                writer.WritePropertyName("criteria");
                writer.WriteStartObject();
                WriteOpenValue(writer, "true", noul.WhenTrue);
                WriteOpenValue(writer, "false", noul.WhenFalse);
                writer.WriteEndObject();
                break;
            case ChoiceQuestion choice:
                writer.WritePropertyName("criteria");
                writer.WriteStartObject();
                foreach (var option in choice.Options)
                {
                    WriteOpenValue(writer, option.Key, option.Value);
                }

                writer.WriteEndObject();
                break;
            case ScoreQuestion score:
                writer.WritePropertyName("criteria");
                writer.WriteStartArray();
                foreach (var level in score.Levels)
                {
                    level.WriteTo(writer);
                }

                writer.WriteEndArray();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteOpenValue(Utf8JsonWriter writer, string name, JsonNode? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
