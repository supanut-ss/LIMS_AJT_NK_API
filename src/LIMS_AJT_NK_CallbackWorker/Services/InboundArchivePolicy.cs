namespace LIMS_AJT_NK_CallbackWorker.Services;

// Helpers for reading COA files from the inbound folder (including sub folders)
// and copying them to the Archive folder before they are moved to Processing and
// sent to the OCR API, so the original data is never lost.
//
// Layout next to the inbound folder (e.g. D:\COA):
//   1_Inbound, 2_Processing, 3_Success, 4_Error, 5_InvalidType, 6_Archive
public static class InboundArchivePolicy
{
    public const string ArchiveFolderName = "6_Archive";

    // The Archive folder is a sibling of the inbound folder, e.g.
    // D:\COA\1_Inbound -> D:\COA\6_Archive.
    public static string ResolveArchiveDirectory(string inboundDirectory)
    {
        var inboundFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inboundDirectory));
        var parent = Path.GetDirectoryName(inboundFullPath);
        return Path.Combine(parent ?? inboundFullPath, ArchiveFolderName);
    }

    // All PDF files under the inbound folder, including sub folders. Files under
    // the archive folder are ignored in case it is configured inside inbound.
    public static List<string> FindPdfFiles(string inboundDirectory, string archiveDirectory)
    {
        var archiveRoot = NormalizeDirectory(archiveDirectory);

        return Directory
            .EnumerateFiles(inboundDirectory, "*.*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetFullPath(file).StartsWith(archiveRoot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetCreationTimeUtc)
            .ToList();
    }

    // The top level folder under inbound that contains the file, or null when the
    // file sits directly in the inbound folder. A folder (with everything inside
    // it) is treated as one unit.
    public static string? GetFolderUnit(string inboundDirectory, string filePath)
    {
        var inboundRoot = NormalizeDirectory(inboundDirectory);
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(inboundRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = fullPath[inboundRoot.Length..];
        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separatorIndex < 0
            ? null
            : Path.Combine(inboundRoot, relative[..separatorIndex]);
    }

    // True when every file in the folder is stable (finished being written).
    public static bool IsFolderReady(string folder, int stableSeconds, DateTime utcNow)
    {
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).ToList();
        return files.Count > 0 && files.All(file => SharedFilePolicy.IsReady(file, stableSeconds, utcNow));
    }

    // Copies one file to the archive folder and verifies the copy.
    public static string CopyFileToArchive(string sourceFile, string archiveDirectory)
    {
        Directory.CreateDirectory(archiveDirectory);
        var destination = GetUniquePath(Path.Combine(archiveDirectory, Path.GetFileName(sourceFile)));

        try
        {
            File.Copy(sourceFile, destination, overwrite: false);
            VerifyCopy(sourceFile, destination);
            return destination;
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }
    }

    // Copies the whole folder (every file and sub folder) to the archive folder,
    // keeping its structure, and verifies each copied file.
    public static string CopyFolderToArchive(string sourceFolder, string archiveDirectory)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
        Directory.CreateDirectory(archiveDirectory);
        var destination = GetUniquePath(Path.Combine(archiveDirectory, Path.GetFileName(source)));

        try
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                File.Copy(file, target, overwrite: false);
                VerifyCopy(file, target);
            }

            return destination;
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    // Removes the folder and any empty sub folders, but only when no files remain.
    public static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)
                || Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any())
            {
                return;
            }

            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Another process may still be using the folder; try again next cycle.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Archive copies keep the original name. Only when that name is already taken
    // (so nothing in Archive is overwritten) a " (n)" suffix is added.
    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void VerifyCopy(string source, string destination)
    {
        if (new FileInfo(source).Length != new FileInfo(destination).Length)
        {
            throw new IOException($"Archive copy size mismatch. Source={source}");
        }
    }

    private static string NormalizeDirectory(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
