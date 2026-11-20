using System.Security.Cryptography;
using LIMS_AJT_NK_CallbackWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LIMS_AJT_NK_API.Tests;

// Simulates the inbound -> archive -> processing flow on a real temp folder
// tree. processFileAsync stands in for "move to 2_Processing + send to API", so
// no API call is made.
public sealed class InboundFolderScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"coa-scan-{Guid.NewGuid():N}");
    private readonly InboundFolderScanner scanner = new(NullLogger.Instance);

    private string Inbound => Path.Combine(root, "1_Inbound");
    private string Processing => Path.Combine(root, "2_Processing");
    private string Archive => Path.Combine(root, "6_Archive");

    public InboundFolderScannerTests()
    {
        Directory.CreateDirectory(Inbound);
        Directory.CreateDirectory(Processing);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_ArchivesEveryFileAndWholeFoldersBeforeMovingToProcessing()
    {
        var hashes = new Dictionary<string, string>
        {
            ["a.pdf"] = Create("a.pdf", 1500),
            ["lot-1/b.pdf"] = Create("lot-1/b.pdf", 2300),
            ["lot-1/c.pdf"] = Create("lot-1/c.pdf", 700),
            ["lot-1/note.txt"] = Create("lot-1/note.txt", 120),
            ["lot-1/sub/d.pdf"] = Create("lot-1/sub/d.pdf", 4100),
            ["lot-2/e.pdf"] = Create("lot-2/e.pdf", 999),
            ["master.xlsx"] = Create("master.xlsx", 300)
        };

        var processed = new List<string>();
        var archiveCheckedBeforeMove = new List<string>();

        await scanner.ScanAsync(Inbound, 5, file =>
        {
            var relative = Path.GetRelativePath(Inbound, file).Replace('\\', '/');

            // The archive copy must already exist and be identical when the file
            // is about to be sent.
            var archived = relative.Contains('/')
                ? Path.Combine(Archive, relative.Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(Archive, relative);
            Assert.True(File.Exists(archived), $"Archive copy missing before processing: {relative}");
            Assert.Equal(hashes[relative], Hash(archived));
            archiveCheckedBeforeMove.Add(relative);

            // Same behaviour as Worker.MoveToFolder.
            File.Move(file, Path.Combine(Processing, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{Path.GetFileName(file)}"));
            processed.Add(relative);
            return Task.CompletedTask;
        }, CancellationToken.None);

        var pdfs = new[] { "a.pdf", "lot-1/b.pdf", "lot-1/c.pdf", "lot-1/sub/d.pdf", "lot-2/e.pdf" };
        Assert.Equal(pdfs.OrderBy(x => x), processed.OrderBy(x => x));
        Assert.Equal(pdfs.OrderBy(x => x), archiveCheckedBeforeMove.OrderBy(x => x));

        // Processing holds all 5 PDFs with unchanged content.
        var inProcessing = Directory.GetFiles(Processing).Select(Hash).OrderBy(x => x);
        var expectedPdfHashes = pdfs.Select(x => hashes[x]).OrderBy(x => x);
        Assert.Equal(expectedPdfHashes, inProcessing);

        // Archive: top-level file by itself, folders in full (incl. non-PDF and
        // nested files) with the original names and identical content.
        Assert.Equal(hashes["a.pdf"], Hash(Path.Combine(Archive, "a.pdf")));
        foreach (var path in new[] { "lot-1/b.pdf", "lot-1/c.pdf", "lot-1/note.txt", "lot-1/sub/d.pdf", "lot-2/e.pdf" })
        {
            Assert.Equal(hashes[path], Hash(Path.Combine(Archive, path.Replace('/', Path.DirectorySeparatorChar))));
        }

        // Folders copied once (no "lot-1 (2)"), and no xlsx copied (not a COA PDF).
        Assert.Equal(
            new[] { "a.pdf", "lot-1", "lot-2" },
            Directory.GetFileSystemEntries(Archive).Select(Path.GetFileName).OrderBy(x => x));

        // Inbound afterwards: PDFs are gone, other files untouched, empty folders removed.
        Assert.False(Directory.Exists(Path.Combine(Inbound, "lot-2")));
        Assert.True(File.Exists(Path.Combine(Inbound, "lot-1", "note.txt")));
        Assert.Equal(hashes["master.xlsx"], Hash(Path.Combine(Inbound, "master.xlsx")));
        Assert.Empty(Directory.EnumerateFiles(Inbound, "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Scan_DoesNotProcessAnythingWhenArchiveCopyFails()
    {
        var hash = Create("a.pdf", 800);
        var folderHash = Create("lot-1/b.pdf", 900);
        // A file named like the archive folder makes copying impossible.
        File.WriteAllText(Archive, "blocker");
        var calls = 0;

        await scanner.ScanAsync(Inbound, 5, _ =>
        {
            calls++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(hash, Hash(Path.Combine(Inbound, "a.pdf")));
        Assert.Equal(folderHash, Hash(Path.Combine(Inbound, "lot-1", "b.pdf")));
        Assert.Empty(Directory.GetFiles(Processing));
    }

    [Fact]
    public async Task Scan_WaitsForFolderStillBeingWritten()
    {
        Create("a.pdf", 500);
        Create("lot-1/b.pdf", 500);
        var fresh = Create("lot-1/c.pdf", 500);
        File.SetLastWriteTimeUtc(Path.Combine(Inbound, "lot-1", "c.pdf"), DateTime.UtcNow);
        _ = fresh;
        var processed = new List<string>();

        await scanner.ScanAsync(Inbound, 30, file =>
        {
            processed.Add(Path.GetRelativePath(Inbound, file));
            File.Move(file, Path.Combine(Processing, Guid.NewGuid().ToString("N")));
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(["a.pdf"], processed);
        Assert.True(File.Exists(Path.Combine(Inbound, "lot-1", "b.pdf")));
        Assert.True(File.Exists(Path.Combine(Inbound, "lot-1", "c.pdf")));
        Assert.False(Directory.Exists(Path.Combine(Archive, "lot-1")));
        Assert.True(File.Exists(Path.Combine(Archive, "a.pdf")));
    }

    [Fact]
    public async Task Scan_KeepsArchiveCopyAndFileWhenSendingFails()
    {
        var failedHash = Create("a.pdf", 600);
        var okHash = Create("b.pdf", 650);

        await scanner.ScanAsync(Inbound, 5, file =>
        {
            if (Path.GetFileName(file) == "a.pdf")
            {
                throw new InvalidOperationException("simulated API failure");
            }

            File.Move(file, Path.Combine(Processing, Guid.NewGuid().ToString("N")));
            return Task.CompletedTask;
        }, CancellationToken.None);

        // The failing file is archived and still in inbound; the next file went through.
        Assert.Equal(failedHash, Hash(Path.Combine(Archive, "a.pdf")));
        Assert.Equal(failedHash, Hash(Path.Combine(Inbound, "a.pdf")));
        Assert.Equal(okHash, Hash(Directory.GetFiles(Processing).Single()));
        Assert.Equal(okHash, Hash(Path.Combine(Archive, "b.pdf")));
    }

    private string Create(string relativePath, int size)
    {
        var path = Path.Combine(Inbound, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        return Hash(path);
    }

    private static string Hash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
