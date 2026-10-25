using System.Text.Json;

namespace Editor.UpdateProbe;

internal sealed record ProbeConfiguration(string FeedDirectory, string DocumentPath, string ResultPath,
    string EvidenceDirectory, string Action, string Fault = "none")
{
    internal static ProbeConfiguration Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return new(Read(root, nameof(FeedDirectory)), Read(root, nameof(DocumentPath)), Read(root, nameof(ResultPath)),
            Read(root, nameof(EvidenceDirectory)), Read(root, nameof(Action)), Read(root, nameof(Fault)));
    }

    private static string Read(JsonElement root, string name)
    {
        return root.GetProperty(name).GetString() ?? throw new InvalidDataException("The probe configuration field is missing: " + name);
    }
}

internal sealed record ProbeReport(int ProcessId, string Version, string StatePath, string Text, bool Dirty, bool CanEdit,
    bool WindowVisible, string Status, string Error, int Progress, string? PackagesDirectory, string? RecoveryError);
