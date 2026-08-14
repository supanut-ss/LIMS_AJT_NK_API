using LIMS_AJT_NK_API.Data;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Tests;

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
