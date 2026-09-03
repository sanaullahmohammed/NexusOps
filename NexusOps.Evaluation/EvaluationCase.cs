using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusOps.Evaluation;

/// <summary>A single labeled dataset entry: a prompt, its expected tool, and that tool's path.</summary>
public sealed record EvaluationCase(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("expectedTool")] string? ExpectedTool,
    [property: JsonPropertyName("expectedPath")] string? ExpectedPath,
    [property: JsonPropertyName("notes")] string? Notes = null);

public static class EvaluationDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads and parses the dataset file. Throws <see cref="DatasetLoadException"/> on any I/O or
    /// parse failure so callers can report a single, actionable message rather than a raw
    /// exception type the dataset author has no reason to recognize.
    /// </summary>
    /// <remarks>
    /// The element type is nullable because valid JSON can contain a bare <c>null</c> array
    /// element (e.g. a stray trailing comma left as <c>[{...}, null]</c> by a hand-edit) — that
    /// deserializes cleanly to a null <see cref="EvaluationCase"/>, not a parse failure. Callers
    /// (<see cref="DatasetValidator"/> and the live runner) must treat a null entry as a
    /// reportable defect, never dereference it.
    /// </remarks>
    public static IReadOnlyList<EvaluationCase?> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new DatasetLoadException($"Dataset file not found: {path}");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new DatasetLoadException($"Could not read dataset file '{path}': {ex.Message}");
        }

        try
        {
            var cases = JsonSerializer.Deserialize<List<EvaluationCase?>>(json, SerializerOptions);
            return cases ?? [];
        }
        catch (JsonException ex)
        {
            throw new DatasetLoadException($"Dataset file '{path}' is not valid JSON: {ex.Message}");
        }
    }
}

public sealed class DatasetLoadException(string message) : Exception(message);
