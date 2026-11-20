using LIMS_AJT_NK_CallbackWorker.Services;

namespace LIMS_AJT_NK_API.Tests;

public sealed class InboundArchivePolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"coa-archive-{Guid.NewGuid():N}");

    public InboundArchivePolicyTests()
    {
        Directory.CreateDirectory(Inbound);
    }

    private string Inbound => Path.Combine(root, "1_Inbound");
    private string Archive => Path.Combine(root, "6_Archive");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveArchiveDirectory_IsSiblingOfInboundFolder()
    {
        Assert.Equal(Archive, InboundArchivePolicy.ResolveArchiveDirectory(Inbound));
        Assert.Equal(Archive, InboundArchivePolicy.ResolveArchiveDirectory(Inbound + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindPdfFiles_ReadsSubFoldersAndIgnoresOtherExtensions()
    {
        var top = Write(Path.Combine(Inbound, "a.pdf"), "1");
        var nested = Write(Path.Combine(Inbound, "lot-1", "b.PDF"), "2");
        var deep = Write(Path.Combine(Inbound, "lot-1", "sub", "c.pdf"), "3");
        Write(Path.Combine(Inbound, "lot-1", "note.txt"), "4");

        var files = InboundArchivePolicy.FindPdfFiles(Inbound, Archive);

        Assert.Equal(
            new[] { top, nested, deep }.OrderBy(x => x),
            files.OrderBy(x => x));
    }

    [Fact]
    public void FindPdfFiles_IgnoresArchiveFolderInsideInbound()
    {
        var insideArchive = Path.Combine(Inbound, "6_Archive");
        Write(Path.Combine(insideArchive, "old.pdf"), "x");
        var kept = Write(Path.Combine(Inbound, "new.pdf"), "y");

        var files = InboundArchivePolicy.FindPdfFiles(Inbound, insideArchive);

        Assert.Equal([kept], files);
    }

    [Fact]
    public void GetFolderUnit_ReturnsTopLevelFolderOrNullForRootFiles()
    {
        Assert.Null(InboundArchivePolicy.GetFolderUnit(Inbound, Path.Combine(Inbound, "a.pdf")));
        Assert.Equal(
            Path.Combine(Inbound, "lot-1"),
            InboundArchivePolicy.GetFolderUnit(Inbound, Path.Combine(Inbound, "lot-1", "sub", "c.pdf")),
            ignoreCase: true);
    }

    [Fact]
    public void CopyFileToArchive_CopiesContentAndKeepsSource()
    {
        var source = Write(Path.Combine(Inbound, "a.pdf"), "pdf-content");

        var copy = InboundArchivePolicy.CopyFileToArchive(source, Archive);

        Assert.True(File.Exists(source));
        Assert.Equal(Path.Combine(Archive, "a.pdf"), copy);
        Assert.Equal("pdf-content", File.ReadAllText(copy));
    }

    [Fact]
    public void CopyFileToArchive_KeepsExistingArchiveFileWhenNameIsTaken()
    {
        var first = Write(Path.Combine(Inbound, "a.pdf"), "first");
        var firstCopy = InboundArchivePolicy.CopyFileToArchive(first, Archive);
        File.WriteAllText(first, "second");

        var secondCopy = InboundArchivePolicy.CopyFileToArchive(first, Archive);

        Assert.Equal(Path.Combine(Archive, "a.pdf"), firstCopy);
        Assert.Equal(Path.Combine(Archive, "a (2).pdf"), secondCopy);
        Assert.Equal("first", File.ReadAllText(firstCopy));
        Assert.Equal("second", File.ReadAllText(secondCopy));
    }

    [Fact]
    public void CopyFolderToArchive_CopiesWholeFolderWithStructure()
    {
        Write(Path.Combine(Inbound, "lot-1", "b.pdf"), "b");
        Write(Path.Combine(Inbound, "lot-1", "note.txt"), "note");
        Write(Path.Combine(Inbound, "lot-1", "sub", "c.pdf"), "c");
        Directory.CreateDirectory(Path.Combine(Inbound, "lot-1", "empty"));

        var copy = InboundArchivePolicy.CopyFolderToArchive(Path.Combine(Inbound, "lot-1"), Archive);

        Assert.Equal(Path.Combine(Archive, "lot-1"), copy);
        Assert.Equal("b", File.ReadAllText(Path.Combine(copy, "b.pdf")));
        Assert.Equal("note", File.ReadAllText(Path.Combine(copy, "note.txt")));
        Assert.Equal("c", File.ReadAllText(Path.Combine(copy, "sub", "c.pdf")));
        Assert.True(Directory.Exists(Path.Combine(copy, "empty")));
        Assert.True(File.Exists(Path.Combine(Inbound, "lot-1", "b.pdf")));
    }

    [Fact]
    public void IsFolderReady_IsFalseWhileAnyFileIsStillBeingWritten()
    {
        Write(Path.Combine(Inbound, "lot-1", "b.pdf"), "b");
        var fresh = Write(Path.Combine(Inbound, "lot-1", "c.pdf"), "c");
        File.SetLastWriteTimeUtc(Path.Combine(Inbound, "lot-1", "b.pdf"), DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        Assert.False(InboundArchivePolicy.IsFolderReady(Path.Combine(Inbound, "lot-1"), 30, DateTime.UtcNow));
        Assert.True(InboundArchivePolicy.IsFolderReady(
            Path.Combine(Inbound, "lot-1"), 30, DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void DeleteIfEmpty_KeepsFolderWithFilesAndRemovesEmptyOne()
    {
        var withFile = Path.Combine(Inbound, "keep");
        Write(Path.Combine(withFile, "x.txt"), "x");
        var empty = Path.Combine(Inbound, "gone");
        Directory.CreateDirectory(Path.Combine(empty, "inner"));

        InboundArchivePolicy.DeleteIfEmpty(withFile);
        InboundArchivePolicy.DeleteIfEmpty(empty);

        Assert.True(Directory.Exists(withFile));
        Assert.False(Directory.Exists(empty));
    }

    private static string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
