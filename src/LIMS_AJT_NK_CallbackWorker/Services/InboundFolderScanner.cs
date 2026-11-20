namespace LIMS_AJT_NK_CallbackWorker.Services;

// Reads the PDF files in the inbound folder (including sub folders), copies them
// to the Archive folder first, and only then hands each file to
// processFileAsync (which moves it to Processing and sends it to the API).
//
// - A file placed directly in inbound is copied on its own.
// - A file inside a sub folder: the whole top level folder is copied once, then
//   the PDFs inside are still processed file by file.
// - If the copy fails or the folder is still being written, nothing is sent and
//   the files stay in inbound for the next cycle.
public sealed class InboundFolderScanner(ILogger logger)
{
    public async Task ScanAsync(
        string inboundDirectory,
        int fileStableSeconds,
        Func<string, Task> processFileAsync,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(inboundDirectory))
        {
            return;
        }

        var archiveDirectory = InboundArchivePolicy.ResolveArchiveDirectory(inboundDirectory);
        var inboundFiles = InboundArchivePolicy.FindPdfFiles(inboundDirectory, archiveDirectory);

        // Folder units (top level sub folders of inbound). Value = true when the
        // folder is safely archived, false when it must wait for the next cycle.
        var folderUnits = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var inboundFile in inboundFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderUnit = InboundArchivePolicy.GetFolderUnit(inboundDirectory, inboundFile);

            try
            {
                if (folderUnit is null)
                {
                    if (!SharedFilePolicy.IsReady(inboundFile, fileStableSeconds, DateTime.UtcNow))
                    {
                        continue;
                    }

                    var archivedFile = InboundArchivePolicy.CopyFileToArchive(inboundFile, archiveDirectory);
                    logger.LogInformation(
                        "Archived inbound file before processing. File={File}, Archive={Archive}",
                        inboundFile,
                        archivedFile);
                }
                else if (!EnsureFolderArchived(folderUnit, fileStableSeconds, archiveDirectory, folderUnits)
                    || !SharedFilePolicy.IsReady(inboundFile, fileStableSeconds, DateTime.UtcNow))
                {
                    continue;
                }

                await processFileAsync(inboundFile);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                logger.LogInformation(ex, "File was already claimed or became unavailable. File={File}", inboundFile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process inbound file. File={File}", inboundFile);
            }
        }

        // Remove sub folders that no longer contain any file after processing.
        foreach (var folderUnit in folderUnits.Keys)
        {
            InboundArchivePolicy.DeleteIfEmpty(folderUnit);
        }
    }

    // Copies the whole folder to Archive the first time one of its files is about
    // to be processed. Returns false when the folder must wait for the next cycle
    // (still being written, or the copy failed), so nothing is sent without a copy.
    private bool EnsureFolderArchived(
        string folderUnit,
        int fileStableSeconds,
        string archiveDirectory,
        Dictionary<string, bool> folderUnits)
    {
        if (folderUnits.TryGetValue(folderUnit, out var archived))
        {
            return archived;
        }

        if (!InboundArchivePolicy.IsFolderReady(folderUnit, fileStableSeconds, DateTime.UtcNow))
        {
            folderUnits[folderUnit] = false;
            return false;
        }

        try
        {
            var archivedFolder = InboundArchivePolicy.CopyFolderToArchive(folderUnit, archiveDirectory);
            logger.LogInformation(
                "Archived inbound folder before processing. Folder={Folder}, Archive={Archive}",
                folderUnit,
                archivedFolder);
            folderUnits[folderUnit] = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to archive inbound folder; it will be retried. Folder={Folder}", folderUnit);
            folderUnits[folderUnit] = false;
            return false;
        }
    }
}
