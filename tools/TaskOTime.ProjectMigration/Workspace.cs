using System.Security.Cryptography;

namespace TaskOTime.ProjectMigration;

internal static class Workspace
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "artifacts", "packages", "node_modules", "TestResults"
    };

    public static bool IsWithin(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void RejectLinkedAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException($"Symlink/junction workspaces are unsupported: {current.FullName}");
    }

    public static SortedDictionary<string, byte[]> Read(string root)
    {
        if (File.Exists(Path.Combine(root, ".projectmigration-incomplete")))
            throw new ArgumentException("Source is an incomplete migration workspace. Remove it and rerun from the original source.");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        void Walk(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException($"Symlink/junction inputs are unsupported: {Path.GetRelativePath(root, entry)}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(entry))) Walk(entry);
                }
                else if (Path.GetFileName(entry) is not ("migration-manifest.json" or ".git"))
                    files.Add(Path.GetRelativePath(root, entry), File.ReadAllBytes(entry));
            }
        }
        Walk(root);
        return files;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static bool IsProject(string path) =>
        (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) &&
        !path.Split(Path.DirectorySeparatorChar).Any(p =>
            p.Equals("tools", StringComparison.OrdinalIgnoreCase) || p.Equals("assessment", StringComparison.OrdinalIgnoreCase));

    public static void Write(string root, IReadOnlyDictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(root);
        foreach (var (relative, bytes) in files)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    public static void Publish(string staging, string destination)
    {
        // Windows file scanners may briefly retain a directory handle after MSBuild exits.
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(staging, destination); return; }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException &&
                                       !Directory.Exists(destination) && !File.Exists(destination))
            {
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    public static void PublishVerified(string staging, string destination, IReadOnlyDictionary<string, byte[]> files)
    {
        try { Publish(staging, destination); }
        catch (IOException) when (!Directory.Exists(destination) && !File.Exists(destination))
        {
            // Some Windows volumes reject directory renames with long descendants. Never expose
            // a success manifest until every verified file has been copied to the final location.
            Directory.CreateDirectory(destination);
            var marker = Path.Combine(destination, ".projectmigration-incomplete");
            try
            {
                File.WriteAllText(marker, "Publication in progress. This is not a completed workspace.");
                Write(destination, files);
                File.Copy(Path.Combine(staging, "migration-manifest.json"), Path.Combine(destination, "migration-manifest.json"));
                File.Delete(marker);
            }
            catch
            {
                Directory.Delete(destination, recursive: true);
                throw;
            }
        }
    }
}
