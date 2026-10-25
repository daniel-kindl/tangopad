namespace Editor.App.Services;

internal enum UnsavedDecision { Cancel, Discard, Save }

internal enum ConflictDecision { Cancel, Reload, Overwrite }

internal interface IEditorDialogs
{
    Task<string?> ChooseOpenAsync();
    Task<string?> ChooseSaveAsync(string? destination);
    Task<UnsavedDecision> ConfirmUnsavedAsync();
    Task<ConflictDecision> ConfirmConflictAsync(string destination);
    Task ShowErrorAsync(string message);
}
