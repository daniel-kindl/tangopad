using System.ComponentModel;
using AvaloniaEdit;
using Editor.App.ViewModels;

namespace Editor.App.Views;

internal sealed class EditorBufferAdapter : ISearchBuffer
{
    private readonly TextEditor editor;
    private readonly EditorViewModel viewModel;

    internal EditorBufferAdapter(TextEditor editor, EditorViewModel viewModel)
    {
        this.editor = editor;
        this.viewModel = viewModel;
        editor.TextChanged += OnTextChanged;
        editor.TextArea.SelectionChanged += OnSelectionChanged;
        viewModel.PropertyChanged += OnStateChanged;
        RefreshDocument();
    }

    public event EventHandler? Changed;
    public string Text => viewModel.Text;
    public int SelectionStart => editor.SelectionStart;
    public int SelectionLength => editor.SelectionLength;

    public void Select(int start, int length)
    {
        editor.Select(start, length);
        editor.TextArea.Caret.BringCaretToView();
    }

    public void Replace(IReadOnlyList<SearchMatch> matches, string replacement)
    {
        using (editor.Document.RunUpdate())
        {
            foreach (SearchMatch match in matches.Reverse())
            {
                editor.Document.Replace(match.Start, match.Length, replacement);
            }
        }

        if (matches.Count != 0)
        {
            Select(matches[0].Start, replacement.Length);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextChanged(object? sender, EventArgs args)
    {
        viewModel.Text = editor.Text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.DocumentId))
        {
            RefreshDocument();
        }
        else if (args.PropertyName == nameof(EditorViewModel.Text) && editor.Text != viewModel.Text)
        {
            editor.Text = viewModel.Text;
        }
    }

    private void RefreshDocument()
    {
        editor.Text = viewModel.Text;
        editor.Document.UndoStack.ClearAll();
        editor.CaretOffset = 0;
    }

    internal void Detach()
    {
        editor.TextChanged -= OnTextChanged;
        editor.TextArea.SelectionChanged -= OnSelectionChanged;
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
