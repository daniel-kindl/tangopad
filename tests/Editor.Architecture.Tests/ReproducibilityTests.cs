using Tangopad.BuildVerification;
using Xunit;

namespace Editor.Architecture.Tests;

/// <summary>Checks that the managed output comparison rejects invalid evidence.</summary>
public sealed class ReproducibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tangopad-test-{Guid.NewGuid():N}");

    /// <summary>Checks equal files at distinct paths.</summary>
    [Fact]
    public void EqualFilesPass()
    {
        (string left, string right) = CreateBuilds();
        Assert.Equal(2, BuildComparison.Compare(left, right, ["Editor.Core.dll", "Editor.Core.pdb"]).Count);
    }

    /// <summary>Checks that a changed DLL or PDB fails.</summary>
    /// <param name="file">The output to change.</param>
    [Theory]
    [InlineData("Editor.Core.dll")]
    [InlineData("Editor.Core.pdb")]
    public void DifferentContentsFail(string file)
    {
        (string left, string right) = CreateBuilds();
        File.AppendAllText(Path.Combine(right, file), "changed");
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BuildComparison.Compare(left, right, ["Editor.Core.dll", "Editor.Core.pdb"]));
        Assert.Contains(file, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Checks that an absent output fails.</summary>
    /// <param name="side">The build with the missing output.</param>
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void MissingOutputFails(string side)
    {
        (string left, string right) = CreateBuilds();
        File.Delete(Path.Combine(side == "left" ? left : right, "Editor.Core.pdb"));
        _ = Assert.Throws<FileNotFoundException>(() => BuildComparison.Compare(left, right, ["Editor.Core.dll", "Editor.Core.pdb"]));
    }

    /// <summary>Checks that a build cannot be compared with itself.</summary>
    [Fact]
    public void SameDirectoryFails()
    {
        (string left, _) = CreateBuilds();
        _ = Assert.Throws<ArgumentException>(() => BuildComparison.Compare(left, Path.Combine(left, "."), ["Editor.Core.dll"]));
    }

    /// <summary>Checks that nested build directories fail.</summary>
    [Fact]
    public void NestedDirectoryFails()
    {
        (string left, _) = CreateBuilds();
        string nested = Path.Combine(left, "nested");
        _ = Assert.Throws<ArgumentException>(() => BuildComparison.Compare(left, nested, ["Editor.Core.dll"]));
    }

    /// <summary>Checks that an absolute output cannot compare one file with itself.</summary>
    [Fact]
    public void AbsoluteOutputFails()
    {
        (string left, string right) = CreateBuilds();
        _ = Assert.Throws<ArgumentException>(() => BuildComparison.Compare(left, right, [Path.Combine(left, "Editor.Core.dll")]));
    }

    /// <summary>Checks that a relative output cannot escape its build directory.</summary>
    [Fact]
    public void OutputOutsideBuildFails()
    {
        (string left, string right) = CreateBuilds();
        _ = Assert.Throws<ArgumentException>(() => BuildComparison.Compare(left, right, [Path.Combine("..", "left", "Editor.Core.dll")]));
    }

    /// <summary>Checks that an empty selection fails.</summary>
    [Fact]
    public void EmptySelectionFails()
    {
        (string left, string right) = CreateBuilds();
        _ = Assert.Throws<ArgumentException>(() => BuildComparison.Compare(left, right, []));
    }

    /// <summary>Checks that a source copy cannot reuse a stale destination.</summary>
    [Fact]
    public void ExistingSourceDestinationFails()
    {
        (string left, string right) = CreateBuilds();
        _ = Assert.Throws<IOException>(() => SourceSnapshot.Copy(left, right, ["Editor.Core.dll"]));
    }

    /// <summary>Checks that prior build outputs cannot enter the snapshot.</summary>
    [Fact]
    public void StaleBuildOutputsAreExcluded()
    {
        (string source, _) = CreateBuilds();
        foreach (string directory in new[] { "bin", "obj", "artifacts", ".git" })
        {
            _ = Directory.CreateDirectory(Path.Combine(source, directory));
            File.WriteAllText(Path.Combine(source, directory, "stale.dll"), "stale output");
        }

        string snapshot = Path.Combine(root, "snapshot");
        SortedDictionary<string, string> inputs = SourceSnapshot.Copy(source, snapshot,
            ["Editor.Core.dll", "bin/stale.dll", "obj/stale.dll", "artifacts/stale.dll", ".git/stale.dll", "deleted.cs"]);
        Assert.Equal(["Editor.Core.dll"], inputs.Keys);
        Assert.Equal(["Editor.Core.dll"], Directory.GetFiles(snapshot).Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(snapshot));
    }

    /// <summary>Checks that both builds receive the original snapshot after a working file changes.</summary>
    [Fact]
    public void BuildsUseOneSourceSnapshot()
    {
        (string source, _) = CreateBuilds();
        string snapshot = Path.Combine(root, "snapshot");
        SortedDictionary<string, string> inputs = SourceSnapshot.Copy(source, snapshot, ["Editor.Core.dll"]);
        File.WriteAllText(Path.Combine(source, "Editor.Core.dll"), "later working tree change");
        foreach (string name in new[] { "first", "second" })
        {
            string build = Path.Combine(root, name);
            _ = SourceSnapshot.Copy(snapshot, build, inputs.Keys);
            Assert.Equal("managed assembly fixture", File.ReadAllText(Path.Combine(build, "Editor.Core.dll")));
        }
    }

    /// <summary>Checks output selection when MSBuild resolves a different absolute source path.</summary>
    [Fact]
    public void EvaluatedProjectDirectoryAnchorsOutputPaths()
    {
        string evaluatedDirectory = Path.Combine(root, "canonical", "eng", "BuildVerification");
        string targetPath = Path.Combine(evaluatedDirectory, "bin", "Release", "net10.0", "BuildVerification.dll");
        string actual = Reproducibility.RelativeOutputPath(Path.Combine("eng", "BuildVerification", "BuildVerification.csproj"), evaluatedDirectory, targetPath);
        Assert.Equal(Path.Combine("eng", "BuildVerification", "bin", "Release", "net10.0", "BuildVerification.dll"), actual);
    }

    /// <summary>Removes temporary output files.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private (string Left, string Right) CreateBuilds()
    {
        string left = Path.Combine(root, "left");
        string right = Path.Combine(root, "right");
        _ = Directory.CreateDirectory(left);
        _ = Directory.CreateDirectory(right);
        foreach (string directory in new[] { left, right })
        {
            File.WriteAllText(Path.Combine(directory, "Editor.Core.dll"), "managed assembly fixture");
            File.WriteAllText(Path.Combine(directory, "Editor.Core.pdb"), "portable symbols fixture");
        }

        return (left, right);
    }
}
