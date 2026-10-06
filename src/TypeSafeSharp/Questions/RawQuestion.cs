using System.Text.Json.Nodes;

namespace TypeSafeSharp;

// Question.FromJson's result. The writer emits Json as is, so the server sees exactly what the
// caller built, including fields this SDK does not know.
internal sealed class RawQuestion : Question
{
    public RawQuestion(JsonObject json, string type)
        : base(json["instructions"]?.DeepClone())
    {
        Json = json;
        Type = type;
    }

    public override string Type { get; }

    public JsonObject Json { get; }
}
