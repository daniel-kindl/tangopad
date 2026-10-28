using System.Reflection;
using System.Text;
using Xunit;

namespace Editor.App.Tests.Updates;

/// <summary>Proves that a default editor build has no update-test hook, marker or transport.</summary>
public sealed class UpdateTestBoundaryTests
{
    private static readonly string Marker = string.Concat("Tangopad.UpdateTestHook", ".v1");

    /// <summary>The default editor assembly has no update-test metadata or types.</summary>
    [Fact]
    public void DefaultEditorHasNoUpdateTestCode()
    {
        Assembly editor = typeof(App).Assembly;
        Assert.DoesNotContain(editor.GetCustomAttributes<AssemblyMetadataAttribute>(), attribute => attribute.Key == "TangopadUpdateTest");
        Assert.Null(editor.GetType("Editor.App.UpdateTest.UpdateTestHook"));
        Assert.Null(editor.GetType("Editor.App.UpdateTest.LoopbackForwarder"));
        Assert.DoesNotContain(editor.GetTypes(), type => type.Namespace == "Editor.App.UpdateTest");
    }

    /// <summary>The default editor file has no marker in either string encoding.</summary>
    [Fact]
    public void DefaultEditorFileHasNoMarker()
    {
        byte[] content = File.ReadAllBytes(typeof(App).Assembly.Location);
        Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Marker)));
        Assert.Equal(-1, content.AsSpan().IndexOf(Encoding.Unicode.GetBytes(Marker)));
    }
}
