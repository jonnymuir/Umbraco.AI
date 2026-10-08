using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Umbraco.AI.Core.Configuration;

/// <summary>
/// Helper for migrating EF Core migration history records from the shared
/// <c>__EFMigrationsHistory</c> table to the Umbraco AI history table.
/// </summary>
/// <remarks>
/// This is needed when transitioning from the default shared history table to
/// the dedicated AI table. Without this, EF Core would attempt to re-run all
/// previously applied migrations because it cannot find them in the new table.
/// </remarks>
public static class AIMigrationHistoryHelper
{
    private const string OldHistoryTable = "__EFMigrationsHistory";
    private const string MigrationPattern = "%UmbracoAI%";

    /// <summary>
    /// Copies all Umbraco AI migration history records from the shared
    /// <c>__EFMigrationsHistory</c> table to the dedicated AI history table.
    /// </summary>
    /// <param name="connection">The database connection (opened if needed, restored to original state).</param>
    /// <param name="newHistoryTable">The AI migrations history table name.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task MigrateHistoryRecordsAsync(
        DbConnection connection,
        string newHistoryTable,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var isSqlite = connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var openedByUs = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (openedByUs)
            {
                await connection.OpenAsync(cancellationToken);
            }

            if (!await TableExistsAsync(connection, OldHistoryTable, isSqlite, cancellationToken))
            {
                logger?.LogDebug(
                    "No shared {OldTable} table found — skipping history migration for {NewTable}",
                    OldHistoryTable, newHistoryTable);
                return;
            }

            await EnsureHistoryTableExistsAsync(connection, newHistoryTable, isSqlite, cancellationToken);

            var copied = await CopyHistoryRecordsAsync(connection, newHistoryTable, isSqlite, cancellationToken);

            if (copied > 0)
            {
                logger?.LogInformation(
                    "Migrated {Count} history record(s) from {OldTable} to {NewTable}",
                    copied, OldHistoryTable, newHistoryTable);
            }
        }
        finally
        {
            if (openedByUs)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Renames migration IDs in the AI history table, so a migration recorded under one ID is
    /// treated as applied under another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed when the same migration shipped with different IDs on two version lines (for
    /// example a backport that regenerated the migration instead of copying it). A site upgrading
    /// across the lines would otherwise run the migration a second time, and fail on schema that
    /// already exists.
    /// </para>
    /// <para>
    /// A rename only happens when the old ID is recorded and the new one is not. If both are
    /// recorded, the old row is removed. Missing history table or old ID: nothing happens.
    /// </para>
    /// </remarks>
    /// <param name="connection">The database connection (opened if needed, restored to original state).</param>
    /// <param name="historyTable">The AI migrations history table name.</param>
    /// <param name="renames">Old migration ID to new migration ID.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RenameMigrationIdsAsync(
        DbConnection connection,
        string historyTable,
        IReadOnlyDictionary<string, string> renames,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (renames.Count == 0)
        {
            return;
        }

        var isSqlite = connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var openedByUs = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (openedByUs)
            {
                await connection.OpenAsync(cancellationToken);
            }

            if (!await TableExistsAsync(connection, historyTable, isSqlite, cancellationToken))
            {
                return;
            }

            foreach (var (oldId, newId) in renames)
            {
                if (!await MigrationRecordedAsync(connection, historyTable, oldId, cancellationToken))
                {
                    continue;
                }

                using var cmd = connection.CreateCommand();
                AddParameter(cmd, "@oldId", oldId);
                AddParameter(cmd, "@newId", newId);

                cmd.CommandText = await MigrationRecordedAsync(connection, historyTable, newId, cancellationToken)
                    ? $"DELETE FROM [{historyTable}] WHERE [MigrationId] = @oldId"
                    : $"UPDATE [{historyTable}] SET [MigrationId] = @newId WHERE [MigrationId] = @oldId";

                await cmd.ExecuteNonQueryAsync(cancellationToken);

                logger?.LogInformation(
                    "Recorded migration {OldId} as {NewId} in {HistoryTable}",
                    oldId, newId, historyTable);
            }
        }
        finally
        {
            if (openedByUs)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> MigrationRecordedAsync(
        DbConnection connection, string historyTable, string migrationId, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{historyTable}] WHERE [MigrationId] = @id";
        AddParameter(cmd, "@id", migrationId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection, string tableName, bool isSqlite, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();

        cmd.CommandText = isSqlite
            ? $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'"
            : $"SELECT CASE WHEN OBJECT_ID('{tableName}', 'U') IS NOT NULL THEN 1 ELSE 0 END";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task EnsureHistoryTableExistsAsync(
        DbConnection connection, string tableName, bool isSqlite, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();

        cmd.CommandText = isSqlite
            ? $"""
               CREATE TABLE IF NOT EXISTS [{tableName}] (
                   [MigrationId] TEXT NOT NULL PRIMARY KEY,
                   [ProductVersion] TEXT NOT NULL
               )
               """
            : $"""
               IF OBJECT_ID('{tableName}', 'U') IS NULL
               BEGIN
                   CREATE TABLE [{tableName}] (
                       [MigrationId] nvarchar(150) NOT NULL,
                       [ProductVersion] nvarchar(32) NOT NULL,
                       CONSTRAINT [PK_{tableName}] PRIMARY KEY ([MigrationId])
                   )
               END
               """;

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> CopyHistoryRecordsAsync(
        DbConnection connection, string newTable, bool isSqlite, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();

        // Copy all Umbraco AI migration records (Core, Agent, Prompt, Search) into the shared AI history table.
        // All AI migration names contain "UmbracoAI" which distinguishes them from CMS migrations.
        cmd.CommandText = $"""
            INSERT INTO [{newTable}] ([MigrationId], [ProductVersion])
            SELECT [MigrationId], [ProductVersion]
            FROM [{OldHistoryTable}]
            WHERE [MigrationId] LIKE '{MigrationPattern}'
            AND [MigrationId] NOT IN (SELECT [MigrationId] FROM [{newTable}])
            """;

        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
