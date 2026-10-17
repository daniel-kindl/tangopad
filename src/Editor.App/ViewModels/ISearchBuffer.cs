namespace Editor.App.ViewModels;

internal readonly record struct SearchMatch(int Start, int Length);

internal interface ISearchBuffer
{
    event EventHandler? Changed;
    string Text { get; }
    int SelectionStart { get; }
    int SelectionLength { get; }
    void Select(int start, int length);
    void Replace(IReadOnlyList<SearchMatch> matches, string replacement);
}
