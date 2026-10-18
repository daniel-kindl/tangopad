namespace Editor.App.ViewModels;

internal sealed record RecentFileEntry(string Path)
{
    public string Label => $"{System.IO.Path.GetFileName(Path)} ({System.IO.Path.GetDirectoryName(Path)})";
}
