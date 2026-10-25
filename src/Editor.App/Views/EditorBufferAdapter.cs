using System.ComponentModel;
using System.Text;
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
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
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

    public void Replace(IReadOnlyList<SearchReplacement> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        string source = Text;
        int start = replacements[0].Match.Start;
        int end = replacements[^1].Match.Start + replacements[^1].Match.Length;
        int offset = start;
        StringBuilder text = new();
        foreach (SearchReplacement replacement in replacements)
        {
            _ = text.Append(source.AsSpan(offset, replacement.Match.Start - offset));
            _ = text.Append(replacement.Text);
            offset = replacement.Match.Start + replacement.Match.Length;
        }

        using (editor.Document.RunUpdate())
        {
            editor.Document.Replace(start, end - start, text.ToString());
        }

        Select(start, replacements[0].Text.Length);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs args)
    {
        RefreshCaretPosition();
    }

    private void RefreshCaretPosition()
    {
        viewModel.CaretLine = editor.TextArea.Caret.Line;
        viewModel.CaretColumn = editor.TextArea.Caret.Column;
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
        RefreshCaretPosition();
    }

    internal void Detach()
    {
        editor.TextChanged -= OnTextChanged;
        editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        editor.TextArea.SelectionChanged -= OnSelectionChanged;
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
