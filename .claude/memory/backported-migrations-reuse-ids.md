---
name: backported-migrations-reuse-ids
description: A backported EF migration must keep the original line's migration ID; regenerating it with `dotnet ef migrations add` breaks every v17→v18 upgrade with a duplicate-column error
type: gotcha
---

When a schema change is ported between version lines (v17 ⇄ v18), **copy the migration files
as they are**. Keep the same timestamp in the file names and the same `[Migration("...")]` ID.
**Never run `dotnet ef migrations add` again on the other line.** Do this for both the SQL Server
and the SQLite project.

EF records applied migrations by ID in `__UmbracoAIMigrationsHistory`. If the two lines use
different IDs for the same change, a site upgrading from v17 to v18 doesn't see v18's ID in its
history. It runs the migration a second time and fails on schema that already exists. The error
is `Column names in each table must be unique ... specified more than once` (SQL Server error
2705); SQLite gives a duplicate column error. Every Umbraco.AI migration on that database then
stops at startup.

**Why:** this has happened twice.

- **Shipped:** core `UmbracoAI_AddCachedInputTokens` went out with different IDs in 17.3.0
  (`20260731095757` SQL Server / `20260731095759` SQLite) and 18.3.0 (`20260731093410` /
  `20260731093420`). Shipped history can't be renamed, so v18 now maps the v17 IDs to the v18
  IDs before migrating:
  - `RunAIMigrationNotificationHandler.V17MigrationIdRenames`
  - `AIMigrationHistoryHelper.RenameMigrationIdsAsync`
- **Caught before merge:** the v17 port of `UmbracoAIConversations_MessageAgentId` (#468) was
  regenerated. It was fixed by renaming its files and IDs to v18's before it shipped.

Both were reproduced on SQL Server 2022: migrate a database with the v17 line, then run
`dotnet ef database update` from the v18 line on the same database.

**How to apply:**

- **When porting a migration:** copy both providers' `<timestamp>_<Name>.cs`, `.Designer.cs`
  and the model snapshot change. Then confirm the IDs match the source line.
- **Before merging a port, or on any PR that adds a migration while the other line has it:**
  list `Migrations/<timestamp>_*.cs` for each persistence project on `origin/v17/dev` and
  `origin/v18/dev`, and diff the names. They must be identical.
- **If a mismatch has already shipped:** don't rename the shipped files. Add the v17→v18 pair to
  the product's rename map so the newer line records the old ID under its own, as done for
  `AddCachedInputTokens`.
  - Agent, Prompt, Search and Conversations have their own migration handlers. Each needs its own
    map if it ever hits this.
