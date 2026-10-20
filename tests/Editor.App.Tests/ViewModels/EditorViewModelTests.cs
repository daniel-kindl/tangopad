using System.Diagnostics;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.Application;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks presentation behavior through the editor commands.</summary>
/// <param name="output">The measurement output.</param>
public sealed class EditorViewModelTests(ITestOutputHelper output)
{
    /// <summary>Only text changes update ending status. Equal text in a new session keeps its status.</summary>
    [Fact]
    public async Task LineEndingsFollowTextAndDocumentTransitions()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs { OpenPath = "/open.md", Decision = UnsavedDecision.Discard },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.Equal(LineEndingStyle.None, viewModel.LineEndings);
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.Text = "a\r\n";
        Assert.Equal(LineEndingStyle.CrLf, viewModel.LineEndings);
        Assert.Contains("LineEndings", notifications);
        notifications.Clear();
        viewModel.Text = "b\r\n";
        Assert.DoesNotContain("LineEndings", notifications);
        notifications.Clear();
        viewModel.CaretColumn = 2;
        viewModel.WordWrap = true;
        viewModel.IncreaseFontSizeCommand.Execute(null);
        Assert.Equal(["CaretColumn", "WordWrap", "FontSize"], notifications);
        Assert.Equal(LineEndingStyle.CrLf, viewModel.LineEndings);
        viewModel.Text = "loaded\r\n\n";
        Assert.Equal(LineEndingStyle.Mixed, viewModel.LineEndings);
        Guid identity = viewModel.DocumentId;
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.NotEqual(identity, viewModel.DocumentId);
        Assert.Equal(LineEndingStyle.Mixed, viewModel.LineEndings);
        Assert.False(viewModel.IsDirty);
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.Equal(LineEndingStyle.None, viewModel.LineEndings);
        viewModel.Text = "\r";
        Assert.Equal(LineEndingStyle.Cr, viewModel.LineEndings);
        viewModel.Text = "\n";
        Assert.Equal(LineEndingStyle.Lf, viewModel.LineEndings);
        viewModel.Text = "plain";
        Assert.Equal(LineEndingStyle.None, viewModel.LineEndings);
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.Equal(LineEndingStyle.None, viewModel.LineEndings);
        Assert.False(viewModel.IsDirty);
    }

    /// <summary>The count uses Unicode scalars and preserves the exact document text.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="expected">The scalar count.</param>
    [Theory]
    [InlineData("", 0)]
    [InlineData("hello", 5)]
    [InlineData("😀", 1)]
    [InlineData("e\u0301", 2)]
    [InlineData("\r\n", 2)]
    [InlineData("a\r\nb\nc\rd", 8)]
    [InlineData("λ café 😀\r\n", 10)]
    public void CharacterCountUsesUnicodeScalars(string text, int expected)
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Guid identity = viewModel.DocumentId;
        Assert.Equal(0, viewModel.CharacterCount);
        viewModel.Text = text;
        Assert.Equal(expected, viewModel.CharacterCount);
        Assert.Equal(text, viewModel.Text);
        Assert.Equal(text, viewModel.Session.Document.Text);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal(expected != 0, viewModel.IsDirty);
    }

    /// <summary>Only changed counts notify consumers. Caret navigation preserves the cached count.</summary>
    [Fact]
    public void CharacterCountNotificationsFollowTextChanges()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.Text = "😀";
        Assert.Equal(1, viewModel.CharacterCount);
        Assert.Contains("CharacterCount", notifications);
        notifications.Clear();
        viewModel.Text = "x";
        Assert.Equal(1, viewModel.CharacterCount);
        Assert.DoesNotContain("CharacterCount", notifications);
        notifications.Clear();
        viewModel.CaretLine = 2;
        viewModel.CaretColumn = 3;
        Assert.Equal(1, viewModel.CharacterCount);
        Assert.Equal(["CaretLine", "CaretColumn"], notifications);
    }

    /// <summary>Words are maximal runs of non-whitespace Unicode scalars in exact source text.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="characters">The expected scalar count.</param>
    /// <param name="words">The expected word count.</param>
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("hello world", 11, 2)]
    [InlineData("  one  two ", 11, 2)]
    [InlineData(" \t\r\n\v\f\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000", 25, 0)]
    [InlineData("a\r\nb\nc\rd\te", 10, 5)]
    [InlineData("a\u00a0b\u2003c\u2028d\u2029e\u0085f\u3000g", 13, 7)]
    [InlineData("😀🚀 e\u0301 \u0301", 7, 3)]
    [InlineData("a\u200bb\ufeffc", 5, 1)]
    [InlineData("# Heading\n**bold text** [link](url)\n- item", 42, 7)]
    [InlineData("```\ncode\tvalue\n```", 18, 4)]
    [InlineData("--- ***", 7, 2)]
    public void WordCountUsesSourceWhitespace(string text, int characters, int words)
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Guid identity = viewModel.DocumentId;
        Assert.Equal(0, viewModel.WordCount);
        viewModel.Text = text;
        Assert.Equal(words, viewModel.WordCount);
        Assert.Equal(characters, viewModel.CharacterCount);
        Assert.Equal(text, viewModel.Session.Document.Text);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal(characters != 0, viewModel.IsDirty);
    }

    /// <summary>Malformed UTF-16 retains the character policy and does not create word boundaries.</summary>
    [Fact]
    public void CountsPreserveUnpairedSurrogates()
    {
        const string text = "a\ud800 b\udc00";
        EditorViewModel viewModel = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *")) { Text = text };
        Assert.Equal(5, viewModel.CharacterCount);
        Assert.Equal(2, viewModel.WordCount);
        Assert.Equal(text, viewModel.Session.Document.Text);
    }

    /// <summary>Text edits and document commands notify word changes. Navigation preserves both counts.</summary>
    [Fact]
    public async Task WordCountNotificationsFollowTextAndDocumentChanges()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs { OpenPath = "/open.md", Decision = UnsavedDecision.Discard },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.Text = "a b";
        Assert.Equal(2, viewModel.WordCount);
        Assert.Contains("WordCount", notifications);
        notifications.Clear();
        viewModel.Text = "abc";
        Assert.Equal(1, viewModel.WordCount);
        Assert.Contains("WordCount", notifications);
        Assert.DoesNotContain("CharacterCount", notifications);
        notifications.Clear();
        viewModel.Text = "xyz";
        Assert.DoesNotContain("WordCount", notifications);
        notifications.Clear();
        viewModel.CaretLine = 2;
        viewModel.CaretColumn = 3;
        Assert.Equal(["CaretLine", "CaretColumn"], notifications);
        Assert.Equal(1, viewModel.WordCount);
        Assert.Equal(3, viewModel.CharacterCount);
        notifications.Clear();
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.Equal(0, viewModel.WordCount);
        Assert.Contains("WordCount", notifications);
        notifications.Clear();
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.Equal(1, viewModel.WordCount);
        Assert.Equal(9, viewModel.CharacterCount);
        Assert.Contains("WordCount", notifications);
        Assert.False(viewModel.IsDirty);
    }

    /// <summary>Generated commands notify consumers and reject overlapping document operations.</summary>
    [Fact]
    public async Task CommandsPublishBusyStateAndPreservePendingEdits()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        List<string?> notifications = [];
        List<bool> availability = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.NewCommand.CanExecuteChanged += (_, _) => availability.Add(viewModel.NewCommand.CanExecute(null));
        viewModel.Text = "first";
        Assert.Equal("Untitled * - Tangopad", viewModel.Title);
        Task saving = viewModel.SaveCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.False(viewModel.NewCommand.CanExecute(null));
        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.SaveAsCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        await viewModel.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        viewModel.Text = "later";
        store.PendingSave.SetResult();
        await saving.ConfigureAwait(true);
        Assert.Equal("first", store.Snapshot);
        Assert.Equal("later", viewModel.Text);
        Assert.Equal("/save.txt * - Tangopad", viewModel.Title);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.NewCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal([false, true], availability);
        Assert.Contains("Title", notifications);
        Assert.Contains("IsDirty", notifications);
        Assert.Contains("IsBusy", notifications);
    }

    /// <summary>The generated cancellation command preserves unsaved text when a store ignores cancellation.</summary>
    [Fact]
    public async Task CancelCommandPreservesTheDirtyBaseline()
    {
        Store store = new();
        EditorViewModel viewModel = new(store, new Dialogs { SavePath = "/save.txt" },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Task saving = viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.CancelCommand.Execute(null);
        store.PendingSave.SetResult();
        await saving.ConfigureAwait(true);
        Assert.Equal("keep", viewModel.Text);
        Assert.True(viewModel.IsDirty);
        Assert.Null(viewModel.Session.Destination);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>A pending save must not acknowledge later edits or permit replacement.</summary>
    [Fact]
    public async Task SaveBeforeNewKeepsEditsMadeDuringSave()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Save, SavePath = "/saved.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "first\r\n"
        };
        Task<bool> replacing = viewModel.NewAsync();
        Assert.Equal("first\r\n", store.Snapshot);
        viewModel.Text = "newer\n";
        store.PendingSave.SetResult();
        Assert.False(await replacing.ConfigureAwait(true));
        Assert.Equal("newer\n", viewModel.Session.Document.Text);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Equal("/saved.txt", viewModel.Session.Destination);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>Cancelled pickers and failed loads preserve the current document.</summary>
    [Fact]
    public async Task OpenCancellationAndFailurePreserveDocument()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Guid id = viewModel.Session.Document.Id;
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        dialogs.OpenPath = "/missing.txt";
        store.LoadError = new IOException("missing");
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.Equal(id, viewModel.Session.Document.Id);
        Assert.Equal("keep", viewModel.Session.Document.Text);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Equal("missing", dialogs.Error);
    }

    /// <summary>A cancelled save and a cancelled confirmation must prevent closing.</summary>
    [Fact]
    public async Task CancelledSaveAndConfirmationPreventClose()
    {
        Store store = new();
        Dialogs dialogs = new();
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        dialogs.Decision = UnsavedDecision.Save;
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        dialogs.SavePath = "/save.txt";
        Task<bool> saving = viewModel.SaveAsync();
        viewModel.Cancel();
        store.PendingSave.SetCanceled(TestContext.Current.CancellationToken);
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Null(dialogs.Error);
    }

    /// <summary>Busy commands cannot replace a document while its save is pending.</summary>
    [Fact]
    public async Task PendingSaveRejectsOtherCommandsAndFailureKeepsBaseline()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Task<bool> saving = viewModel.SaveAsync();
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.False(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.False(await viewModel.SaveAsync().ConfigureAwait(true));
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        store.PendingSave.SetException(new UnauthorizedAccessException("denied"));
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Equal("denied", dialogs.Error);
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>A cancelled pending open preserves the old identity and contents.</summary>
    [Fact]
    public async Task CancelPendingOpenPreservesDocument()
    {
        Store store = new() { PendingLoad = new TaskCompletionSource<string>() };
        Dialogs dialogs = new() { OpenPath = "/input.txt", Decision = UnsavedDecision.Discard };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Guid identity = viewModel.Session.Document.Id;
        Task<bool> opening = viewModel.OpenAsync();
        Assert.False(viewModel.CanEdit);
        viewModel.Cancel();
        store.PendingLoad.SetResult("must not replace");
        Assert.False(await opening.ConfigureAwait(true));
        Assert.Equal("keep", viewModel.Session.Document.Text);
        Assert.Equal(identity, viewModel.Session.Document.Id);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.True(viewModel.CanEdit);
    }

    /// <summary>Saving before close permits close only after the captured text is saved.</summary>
    [Fact]
    public async Task SaveBeforeCloseAndSubsequentSaveUseSuccessfulDestination()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt", Decision = UnsavedDecision.Save };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "saved"
        };
        Task<bool> closing = viewModel.CanCloseAsync();
        Assert.True(viewModel.IsBusy);
        store.PendingSave.SetResult();
        Assert.True(await closing.ConfigureAwait(true));
        Assert.False(viewModel.Session.Document.IsDirty);
        dialogs.SavePath = null;
        viewModel.Text = "again";
        Assert.True(await viewModel.SaveAsync().ConfigureAwait(true));
        Assert.Equal("again", store.Snapshot);
        Assert.Equal("/save.txt", viewModel.Session.Destination);
        Assert.False(viewModel.Session.Document.IsDirty);
    }

    /// <summary>Word Wrap is observable presentation state that survives document replacement.</summary>
    [Fact]
    public async Task WordWrapPreservesDocumentStateAcrossNewAndOpen()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs
        {
            Decision = UnsavedDecision.Discard,
            OpenPath = "/input.md"
        }, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.False(viewModel.WordWrap);
        Guid identity = viewModel.DocumentId;
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.WordWrap = true;
        Assert.Equal(["WordWrap"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        viewModel.Text = "λ😀\r\nline\nlast\r";
        notifications.Clear();
        viewModel.WordWrap = false;
        Assert.Equal(["WordWrap"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("λ😀\r\nline\nlast\r", viewModel.Session.Document.Text);
        Assert.True(viewModel.IsDirty);
        viewModel.WordWrap = true;
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.True(viewModel.WordWrap);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.True(viewModel.WordWrap);
        Assert.Equal("loaded\r\n\n", viewModel.Text);
        Assert.False(viewModel.IsDirty);
    }

    /// <summary>Line numbers are window state and do not change document state or counts.</summary>
    [Fact]
    public async Task LineNumbersPreserveDocumentAcrossNewAndOpen()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs
        {
            Decision = UnsavedDecision.Discard,
            OpenPath = "/input.md"
        }, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.True(viewModel.ShowLineNumbers);
        viewModel.Text = "λ😀\r\nline\nlast\r";
        Guid identity = viewModel.DocumentId;
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.ShowLineNumbers = false;
        Assert.Equal(["ShowLineNumbers"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("λ😀\r\nline\nlast\r", viewModel.Session.Document.Text);
        Assert.True(viewModel.IsDirty);
        notifications.Clear();
        viewModel.ShowLineNumbers = true;
        Assert.Equal(["ShowLineNumbers"], notifications);
        viewModel.ShowLineNumbers = false;
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.False(viewModel.ShowLineNumbers);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.False(viewModel.ShowLineNumbers);
        Assert.Equal("loaded\r\n\n", viewModel.Text);
        Assert.False(viewModel.IsDirty);
        EditorViewModel nextWindow = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.True(nextWindow.ShowLineNumbers);
    }

    /// <summary>Font commands clamp size and notify only presentation changes.</summary>
    [Fact]
    public async Task FontSizeIsBoundedObservableWindowState()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs { OpenPath = "/input.md" },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.Equal(15, viewModel.FontSize);
        Guid identity = viewModel.DocumentId;
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.IncreaseFontSizeCommand.Execute(null);
        Assert.Equal(16, viewModel.FontSize);
        Assert.Equal(["FontSize"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        for (int index = 0; index < 100; index++)
        {
            viewModel.IncreaseFontSizeCommand.Execute(null);
        }

        Assert.Equal(48, viewModel.FontSize);
        notifications.Clear();
        viewModel.IncreaseFontSizeCommand.Execute(null);
        Assert.Empty(notifications);
        for (int index = 0; index < 100; index++)
        {
            viewModel.DecreaseFontSizeCommand.Execute(null);
        }

        Assert.Equal(8, viewModel.FontSize);
        notifications.Clear();
        viewModel.DecreaseFontSizeCommand.Execute(null);
        Assert.Empty(notifications);
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.Equal(8, viewModel.FontSize);
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.Equal(8, viewModel.FontSize);
        viewModel.ResetFontSizeCommand.Execute(null);
        Assert.Equal(15, viewModel.FontSize);
        Assert.Equal("loaded\r\n\n", viewModel.Text);
        Assert.False(viewModel.IsDirty);
    }

    /// <summary>Large updates keep the document counts correct and caret navigation leaves the text unchanged.</summary>
    /// <param name="repetitions">The number of eight-code-unit tokens.</param>
    [Theory]
    [InlineData(125_000)]
    [InlineData(1_250_000)]
    public void DocumentCountsLargeInputs(int repetitions)
    {
        string text = string.Concat(Enumerable.Repeat("a😀e\u0301\r\nx", repetitions));
        string edited = text + "!";
        EditorViewModel viewModel = new(new Store(), new Dialogs(), new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        for (int index = 0; index < 20; index++)
        {
            viewModel.Text = index % 2 == 0 ? text : edited;
        }

        Stopwatch watch = new();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int index = 0; index < 100; index++)
        {
            viewModel.Text = index % 2 == 0 ? text : edited;
        }

        watch.Stop();
        long editBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double editMilliseconds = watch.Elapsed.TotalMilliseconds / 100;
        Assert.Equal((7 * repetitions) + 1, viewModel.CharacterCount);
        Assert.Equal(repetitions + 1, viewModel.WordCount);
        Assert.Equal(LineEndingStyle.CrLf, viewModel.LineEndings);
        watch.Restart();
        for (int index = 0; index < 100_000; index++)
        {
            viewModel.CaretColumn = (index % 100) + 1;
        }

        watch.Stop();
        Assert.Equal((7 * repetitions) + 1, viewModel.CharacterCount);
        Assert.Equal(repetitions + 1, viewModel.WordCount);
        Assert.Equal(LineEndingStyle.CrLf, viewModel.LineEndings);
        Assert.Same(edited, viewModel.Text);
        output.WriteLine($"{text.Length:N0} UTF-16 code units: {editMilliseconds:F3} ms/update, {editBytes / 100} bytes/update; 100,000 caret changes: {watch.Elapsed.TotalMilliseconds:F3} ms.");
    }

    /// <summary>A dropped path shares the operation guard and cancellation behavior of picker Open.</summary>
    [Fact]
    public async Task PathOpenRejectsReentryAndPreservesSessionOnCancellation()
    {
        Store store = new() { PendingLoad = new() };
        Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep me"
        };
        Guid identity = viewModel.DocumentId;
        Task<bool> pending = viewModel.OpenPathAsync("next.md");
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanEdit);
        Assert.False(await viewModel.OpenPathAsync("another.txt").ConfigureAwait(true));
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        viewModel.CancelCommand.Execute(null);
        store.PendingLoad.SetResult("replacement");
        Assert.False(await pending.ConfigureAwait(true));
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("keep me", viewModel.Text);
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
    }

    /// <summary>Cancelled Save As prevents a path-based replacement.</summary>
    [Fact]
    public async Task PathOpenPreservesDocumentWhenSavePickerIsCancelled()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs { Decision = UnsavedDecision.Save },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep me"
        };
        Guid identity = viewModel.DocumentId;
        Assert.False(await viewModel.OpenPathAsync("next.md").ConfigureAwait(true));
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("keep me", viewModel.Text);
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.IsDirty);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal TaskCompletionSource PendingSave { get; } = new();
        internal string? Snapshot { get; private set; }
        internal Exception? LoadError { get; set; }
        internal TaskCompletionSource<string>? PendingLoad { get; init; }

        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return PendingLoad?.Task ?? (LoadError is { } error ? Task.FromException<string>(error) : Task.FromResult("loaded\r\n\n"));
        }

        public Task SaveAsync(string destination, string text, CancellationToken cancellationToken = default)
        {
            Snapshot = text;
            return PendingSave.Task;
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; set; } = UnsavedDecision.Cancel;
        internal string? OpenPath { get; set; }
        internal string? SavePath { get; set; }
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync()
        {
            return Task.FromResult(OpenPath);
        }

        public Task<string?> ChooseSaveAsync(string? destination)
        {
            return Task.FromResult(SavePath);
        }

        public Task<UnsavedDecision> ConfirmUnsavedAsync()
        {
            return Task.FromResult(Decision);
        }

        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}
