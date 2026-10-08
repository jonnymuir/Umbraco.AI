using Microsoft.EntityFrameworkCore;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Persistence;
using Umbraco.AI.Persistence.AuditLog;
using Umbraco.AI.Tests.Common.Fixtures;

namespace Umbraco.AI.Tests.Unit.Repositories;

public class EFCoreAIAuditLogRepositoryTests : IClassFixture<EFCoreTestFixture>
{
    private readonly EFCoreTestFixture _fixture;

    public EFCoreAIAuditLogRepositoryTests(EFCoreTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static EFCoreAIAuditLogRepository CreateRepository(UmbracoAIDbContext context)
        => new(new TestEFCoreScopeProvider(() => context));

    private static AIAuditLogEntity CreateEntity(AIAuditLogStatus status, DateTime startTime) => new()
    {
        Id = Guid.NewGuid(),
        StartTime = startTime,
        Status = (int)status,
        ProfileId = Guid.NewGuid(),
        ProfileAlias = "test-profile",
        ProviderId = "fake",
        ModelId = "fake-model",
    };

    #region FailRunningOlderThanAsync

    [Fact]
    public async Task FailRunningOlderThanAsync_OnlyFailsRunningEntriesStartedBeforeThreshold()
    {
        // Arrange
        var threshold = DateTime.UtcNow.AddHours(-1);
        var staleRunning = CreateEntity(AIAuditLogStatus.Running, threshold.AddMinutes(-5));
        var recentRunning = CreateEntity(AIAuditLogStatus.Running, threshold.AddMinutes(5));
        var oldSucceeded = CreateEntity(AIAuditLogStatus.Succeeded, threshold.AddMinutes(-5));

        await using (var context = _fixture.CreateContext())
        {
            context.AuditLogs.AddRange(staleRunning, recentRunning, oldSucceeded);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(_fixture.CreateContext());

        // Act
        await repository.FailRunningOlderThanAsync(threshold, "interrupted", CancellationToken.None);

        // Assert
        await using var assertContext = _fixture.CreateContext();
        var ids = new[] { staleRunning.Id, recentRunning.Id, oldSucceeded.Id };
        var statuses = await assertContext.AuditLogs
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => (AIAuditLogStatus)t.Status);

        statuses[staleRunning.Id].ShouldBe(AIAuditLogStatus.Failed);
        statuses[recentRunning.Id].ShouldBe(AIAuditLogStatus.Running);
        statuses[oldSucceeded.Id].ShouldBe(AIAuditLogStatus.Succeeded);
    }

    [Fact]
    public async Task FailRunningOlderThanAsync_RecordsErrorAndLeavesEndTimeUnset()
    {
        // Arrange
        var threshold = DateTime.UtcNow.AddHours(-1);
        var staleRunning = CreateEntity(AIAuditLogStatus.Running, threshold.AddMinutes(-5));

        await using (var context = _fixture.CreateContext())
        {
            context.AuditLogs.Add(staleRunning);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(_fixture.CreateContext());

        // Act
        await repository.FailRunningOlderThanAsync(threshold, "interrupted", CancellationToken.None);

        // Assert
        await using var assertContext = _fixture.CreateContext();
        var result = await assertContext.AuditLogs.SingleAsync(t => t.Id == staleRunning.Id);

        result.ErrorCategory.ShouldBe((int)AIAuditLogErrorCategory.Unknown);
        result.ErrorMessage.ShouldBe("interrupted");
        result.EndTime.ShouldBeNull();
    }

    #endregion
}
