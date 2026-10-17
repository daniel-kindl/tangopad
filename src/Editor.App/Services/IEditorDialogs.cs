namespace Editor.App.Services;

internal enum UnsavedDecision { Cancel, Discard, Save }

internal interface IEditorDialogs
{
    Task<string?> ChooseOpenAsync();
    Task<string?> ChooseSaveAsync(string? destination);
    Task<UnsavedDecision> ConfirmUnsavedAsync();
    Task ShowErrorAsync(string message);
}
