# Copilot Prompt: Create the TaskOTime SQL Server Database Script

Create or maintain `src\TaskOTime\TaskOTime.DataLayer\DbGenerationScript.sql`.

The database schema is derived from the existing VB entity classes in the original app under:

```text
src\CommandBinding\Time-O-Task\Time-O-Task.Entities\Entities
```

## Goal

Generate a SQL Server 2012-compatible creation script for a database named `TaskOTime`.

The script should:

1. Create the `TaskOTime` database if needed.
2. Set compatibility level `110`.
3. Create tables based on the original VB entity schema.
4. Use `uniqueidentifier` primary keys with `NEWSEQUENTIALID()` defaults.
5. Add all required foreign key constraints after table creation.
6. Add useful unique and non-unique indexes for keys, relationships, dates, lookup fields, and common searches.
7. Add join tables where entity collections imply many-to-many relationships, such as tag assignments.
8. Add the GUID reservation helper table and stored procedure needed for reserving sequential GUID blocks.

## Foreign Keys

Foreign keys must be added explicitly for user, project, category, symbol, task, time-item, and tag relationships.

Use cautious delete behavior. Prefer `ON DELETE NO ACTION` for normal relationships, especially self-references and shared user/project links. Use cascading deletes only for pure join tables where the owner-side delete is unambiguous.

## Indexes

Add indexes based on the VB entity attributes and practical database usage:

- Unique indexes for natural uniqueness, such as user identifiers, emails, category names per user, task-list names per project, tags per user, and project sharing per user.
- Non-unique indexes for foreign keys, dates, lookup keys, tenant lead search fields, web-link domain/title fields, and time-entry query patterns.
- Avoid indexing large description or note text fields.

## Stored Procedures

Do not add views.

Stored procedures should be avoided except for the GUID reservation mechanism. The reservation procedure should return a block of reserved sequential GUIDs for later EF/client use, backed by a helper table using `NEWSEQUENTIALID()`.

## Expected Output

The final `DbGenerationScript.sql` should be a complete, SQL Server 2012-compatible schema creation script for the TaskOTime database, based on the original VB entity schema and suitable for later EF Database First work.
