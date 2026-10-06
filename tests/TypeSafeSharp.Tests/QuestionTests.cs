using System.Text.Json.Nodes;

namespace TypeSafeSharp.Tests;

public sealed class QuestionTests
{
    [Fact]
    public void Noul_SetsTypeAndValues()
    {
        var q = Question.Noul("Is the ticket urgent?", "needs a reply today", "can wait");

        Assert.Equal("noul", q.Type);
        Assert.Equal("Is the ticket urgent?", q.Instructions!.GetValue<string>());
        Assert.Equal("needs a reply today", q.WhenTrue!.GetValue<string>());
        Assert.Equal("can wait", q.WhenFalse!.GetValue<string>());
    }

    [Fact]
    public void Noul_WithoutCriteria_LeavesThemNull()
    {
        var q = Question.Noul("Is it spam?");

        Assert.Null(q.WhenTrue);
        Assert.Null(q.WhenFalse);
    }

    [Fact]
    public void Choice_Names_KeepsOrderWithNullDescriptions()
    {
        var q = Question.Choice("Which team?", "billing", "support", "sales");

        Assert.Equal("choice", q.Type);
        Assert.Equal(["billing", "support", "sales"], q.Options.Keys);
        Assert.All(q.Options.Values, Assert.Null);
    }

    [Fact]
    public void Choice_Map_ClonesDescriptions()
    {
        var description = JsonValue.Create("money questions");
        var q = Question.Choice("Which team?", new Dictionary<string, JsonNode?> { ["billing"] = description, ["other"] = null });

        Assert.Equal("money questions", q.Options["billing"]!.GetValue<string>());
        Assert.NotSame(description, q.Options["billing"]);
        Assert.Null(q.Options["other"]);
    }

    [Fact]
    public void Score_SetsLevels()
    {
        var q = Question.Score("How angry is the customer?", "calm", "annoyed", "furious");

        Assert.Equal("score", q.Type);
        Assert.Equal(["calm", "annoyed", "furious"], q.Levels.Select(l => l.GetValue<string>()));
    }

    [Fact]
    public void Score_NullLevels_ThrowsArgumentNull()
        => Assert.Equal("levels", Assert.Throws<ArgumentNullException>(() => Question.Score("x", null!)).ParamName);

    [Fact]
    public void Score_OneLevel_Throws()
        => Assert.Equal("levels", Assert.Throws<ArgumentException>(() => Question.Score("x", "only")).ParamName);

    [Fact]
    public void Score_NullLevel_Throws()
        => Assert.Equal("levels", Assert.Throws<ArgumentException>(() => Question.Score("x", "low", null!)).ParamName);

    [Fact]
    public void Choice_NullNames_ThrowsArgumentNull()
        => Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => Question.Choice("x", (string[])null!)).ParamName);

    [Fact]
    public void Choice_NullMap_ThrowsArgumentNull()
        => Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => Question.Choice("x", (IReadOnlyDictionary<string, JsonNode?>)null!)).ParamName);

    public static TheoryData<string[]> BadNames => new()
    {
        { [] },
        { ["a", ""] },
        { ["a", null!] },
        { ["a", "a"] },
    };

    [Theory]
    [MemberData(nameof(BadNames))]
    public void Choice_BadNames_Throw(string[] options)
        => Assert.Equal("options", Assert.Throws<ArgumentException>(() => Question.Choice("x", options)).ParamName);

    [Fact]
    public void Choice_EmptyMap_Throws()
        => Assert.Equal("options", Assert.Throws<ArgumentException>(() => Question.Choice("x", new Dictionary<string, JsonNode?>())).ParamName);

    [Fact]
    public void Choice_EmptyKeyInMap_Throws()
        => Assert.Equal("options", Assert.Throws<ArgumentException>(() => Question.Choice("x", new Dictionary<string, JsonNode?> { [""] = null })).ParamName);

    [Fact]
    public void Factories_CloneCallerNodes()
    {
        var instructions = new JsonObject { ["text"] = "original" };
        var level = new JsonObject { ["label"] = "low" };

        var q = Question.Score(instructions, level, "high");
        instructions["text"] = "changed";
        level["label"] = "changed";

        Assert.Equal("original", q.Instructions!["text"]!.GetValue<string>());
        Assert.Equal("low", q.Levels[0]["label"]!.GetValue<string>());
        Assert.Null(instructions.Parent);
        Assert.Null(level.Parent);
    }

    [Fact]
    public void FromJson_KeepsTypeAndClones()
    {
        var raw = new JsonObject
        {
            ["type"] = "ranking",
            ["instructions"] = "Rank these",
            ["criteria"] = new JsonArray("a", "b"),
        };

        var q = Question.FromJson(raw);
        raw["type"] = "changed";
        raw["instructions"] = "changed";

        Assert.Equal("ranking", q.Type);
        Assert.Equal("Rank these", q.Instructions!.GetValue<string>());
        var json = Assert.IsType<RawQuestion>(q).Json;
        Assert.Equal("ranking", json["type"]!.GetValue<string>());
        Assert.Equal(2, json["criteria"]!.AsArray().Count);
    }

    [Fact]
    public void FromJson_KnownType_IsNotRejected()
        => Assert.Equal("noul", Question.FromJson(new JsonObject { ["type"] = "noul" }).Type);

    [Fact]
    public void FromJson_Null_ThrowsArgumentNull()
        => Assert.Equal("question", Assert.Throws<ArgumentNullException>(() => Question.FromJson(null!)).ParamName);

    public static TheoryData<string> BadTypes => new()
    {
        """{"instructions": "x"}""",
        """{"type": ""}""",
        """{"type": 3}""",
        """{"type": null}""",
        """{"type": ["noul"]}""",
    };

    [Theory]
    [MemberData(nameof(BadTypes))]
    public void FromJson_BadType_Throws(string json)
    {
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("question", Assert.Throws<ArgumentException>(() => Question.FromJson(obj)).ParamName);
    }
}
