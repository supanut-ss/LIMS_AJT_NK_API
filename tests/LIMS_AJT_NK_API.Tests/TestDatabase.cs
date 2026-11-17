using LIMS_AJT_NK_API.Data;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Tests;

internal static class RemapProcedureScript
{
    // The test DB is created fresh from the EF model (EnsureCreatedAsync), which
    // does not run database/*.sql migration scripts. dbo.usp_ocr_callback_remap
    // lives only in that script, so tests that exercise OcrCallbackInterfaceService
    // need it deployed here to exercise the real stored procedure, not a stub.
    public static readonly string Sql = LoadScript();

    private static string LoadScript()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, "database", "20261117_add_usp_ocr_callback_remap.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException(
            "Could not locate database/20261117_add_usp_ocr_callback_remap.sql relative to the test assembly.");
    }
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string connectionString;

    private TestDatabase(string connectionString, ApplicationDbContext context)
    {
        this.connectionString = connectionString;
        Context = context;
    }

    public ApplicationDbContext Context { get; }

    public static async Task<TestDatabase> CreateAsync(bool useFailingContext = false)
    {
        var databaseName = $"LimsApiTests_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = CreateOptions(connectionString);
        ApplicationDbContext context = useFailingContext
            ? new FailingApplicationDbContext(options)
            : new ApplicationDbContext(options);

        await context.Database.EnsureCreatedAsync();
        await DeployRemapProcedureAsync(context);
        return new TestDatabase(connectionString, context);
    }

    public ApplicationDbContext CreateContext()
    {
        return new ApplicationDbContext(CreateOptions(connectionString));
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await using var cleanupContext = CreateContext();
        await cleanupContext.Database.EnsureDeletedAsync();
    }

    private static async Task DeployRemapProcedureAsync(ApplicationDbContext context)
    {
        var batches = RemapProcedureScript.Sql
            .Split("\nGO\n", StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith("USE ", StringComparison.OrdinalIgnoreCase));

        foreach (var batch in batches)
        {
            await context.Database.ExecuteSqlRawAsync(batch);
        }
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }
}

internal sealed class FailingApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContext(options)
{
    public bool FailAfterSave { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var saved = await base.SaveChangesAsync(cancellationToken);
        if (FailAfterSave)
        {
            throw new InvalidOperationException("Simulated database failure after SaveChanges.");
        }

        return saved;
    }
}
