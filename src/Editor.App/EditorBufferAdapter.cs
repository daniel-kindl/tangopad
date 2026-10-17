using System.ComponentModel;
using AvaloniaEdit;

namespace Editor.App;

internal sealed class EditorBufferAdapter
{
    private readonly TextEditor editor;
    private readonly EditorViewModel viewModel;

    internal EditorBufferAdapter(TextEditor editor, EditorViewModel viewModel)
    {
        this.editor = editor;
        this.viewModel = viewModel;
        editor.TextChanged += OnTextChanged;
        viewModel.PropertyChanged += OnStateChanged;
        RefreshDocument();
    }

    private void OnTextChanged(object? sender, EventArgs args)
    {
        viewModel.Text = editor.Text;
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
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
