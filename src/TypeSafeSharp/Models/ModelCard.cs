namespace TypeSafeSharp;

/// <summary>One model from <c>GET /v1/models</c>.</summary>
public sealed class ModelCard
{
    internal ModelCard(string name, string description, string releaseDate)
    {
        Name = name;
        Description = description;
        ReleaseDate = releaseDate;
    }

    /// <summary>The model id to put in <see cref="SystemOneRequest.Model"/>, for example <c>jev-1.13.0</c>.</summary>
    public string Name { get; }

    /// <summary>The server's description of the model.</summary>
    public string Description { get; }

    /// <summary>The release date as sent, <c>YYYY-MM-DD</c>. A string because <c>DateOnly</c> is not on netstandard2.0.</summary>
    public string ReleaseDate { get; }
}
