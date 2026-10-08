using Microsoft.Data.Sqlite;
using Umbraco.AI.Core.Configuration;

namespace Umbraco.AI.Tests.Unit.Configuration;

public class AIMigrationHistoryHelperRenameTests : IAsyncLifetime
{
    private const string HistoryTable = "__UmbracoAIMigrationsHistory";
    private const string OldId = "20260731095759_UmbracoAI_AddCachedInputTokens";
    private const string NewId = "20260731093420_UmbracoAI_AddCachedInputTokens";

    private static readonly Dictionary<string, string> Renames = new() { [OldId] = NewId };

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync() => await _connection.OpenAsync();

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task RenameMigrationIdsAsync_OldIdRecorded_RecordsItUnderNewId()
    {
        await CreateHistoryTableAsync("20260112111534_UmbracoAI_AddAIAuditLog", OldId);

        await AIMigrationHistoryHelper.RenameMigrationIdsAsync(_connection, HistoryTable, Renames);

        (await GetRecordedIdsAsync()).ShouldBe(["20260112111534_UmbracoAI_AddAIAuditLog", NewId], ignoreOrder: true);
    }

    [Fact]
    public async Task RenameMigrationIdsAsync_BothIdsRecorded_RemovesOldId()
    {
        await CreateHistoryTableAsync(OldId, NewId);

        await AIMigrationHistoryHelper.RenameMigrationIdsAsync(_connection, HistoryTable, Renames);

        (await GetRecordedIdsAsync()).ShouldBe([NewId]);
    }

    [Fact]
    public async Task RenameMigrationIdsAsync_OldIdNotRecorded_LeavesHistoryAlone()
    {
        await CreateHistoryTableAsync("20260112111534_UmbracoAI_AddAIAuditLog");

        await AIMigrationHistoryHelper.RenameMigrationIdsAsync(_connection, HistoryTable, Renames);

        (await GetRecordedIdsAsync()).ShouldBe(["20260112111534_UmbracoAI_AddAIAuditLog"]);
    }

    [Fact]
    public async Task RenameMigrationIdsAsync_NoHistoryTable_DoesNothing()
    {
        await Should.NotThrowAsync(() => AIMigrationHistoryHelper.RenameMigrationIdsAsync(_connection, HistoryTable, Renames));
    }

    [Fact]
    public async Task RenameMigrationIdsAsync_ClosedConnection_RenamesAndRestoresClosedState()
    {
        // A shared in-memory database, kept alive by a second open connection, so the helper can
        // open and close its own connection to the same data.
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        await CreateHistoryTableAsync(keepAlive, OldId);
        await using var closed = new SqliteConnection(connectionString);

        await AIMigrationHistoryHelper.RenameMigrationIdsAsync(closed, HistoryTable, Renames);

        closed.State.ShouldBe(System.Data.ConnectionState.Closed);
        (await GetRecordedIdsAsync(keepAlive)).ShouldBe([NewId]);
    }

    private Task CreateHistoryTableAsync(params string[] migrationIds)
        => CreateHistoryTableAsync(_connection, migrationIds);

    private Task<List<string>> GetRecordedIdsAsync() => GetRecordedIdsAsync(_connection);

    private static async Task CreateHistoryTableAsync(SqliteConnection connection, params string[] migrationIds)
    {
        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE TABLE [{HistoryTable}] ([MigrationId] TEXT NOT NULL PRIMARY KEY, [ProductVersion] TEXT NOT NULL)";
        await create.ExecuteNonQueryAsync();

        foreach (var id in migrationIds)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO [{HistoryTable}] VALUES (@id, '10.0.0')";
            insert.Parameters.AddWithValue("@id", id);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<string>> GetRecordedIdsAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT [MigrationId] FROM [{HistoryTable}]";

        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }
}
