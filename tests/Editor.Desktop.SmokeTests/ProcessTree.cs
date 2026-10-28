using System.Globalization;

namespace Editor.Desktop.SmokeTests;

/// <summary>Finds the processes that a launcher started.</summary>
internal static class ProcessTree
{
    /// <summary>Returns the root process and all of its descendants.</summary>
    internal static HashSet<long> WithDescendants(long root, IEnumerable<(long Id, long Parent)> processes)
    {
        (long Id, long Parent)[] snapshot = [.. processes];
        HashSet<long> selected = [root];
        bool added = true;
        while (added)
        {
            added = false;
            foreach ((long id, long parent) in snapshot)
            {
                if (selected.Contains(parent) && selected.Add(id))
                {
                    added = true;
                }
            }
        }

        return selected;
    }

    /// <summary>Reads the Linux process table from /proc.</summary>
    internal static IEnumerable<(long Id, long Parent)> ReadLinux()
    {
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!long.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out long id))
            {
                continue;
            }

            string status;
            try
            {
                status = File.ReadAllText(Path.Combine(directory, "stat"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // The command name can contain spaces, so read the fields after its closing parenthesis.
            string[] fields = status[(status.LastIndexOf(')') + 2)..].Split(' ');
            if (fields.Length > 1 && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long parent))
            {
                yield return (id, parent);
            }
        }
    }
}
