# Database Design

WCM_Master_Plan.md section 7 defines the schema. The live schema is in
`src/WCM.Infrastructure/SQLite/Migrations/`.

## Refinements to the plan

- `SchemaMigrations` records each applied version and its checksum.
- Paths use `COLLATE NOCASE` to match Windows path comparison.
- Extra indexes cover foreign keys and scan item path lookups.

## Failure behavior

Startup stops, and leaves the database file in place, when the file is corrupt, an integrity or
foreign-key check fails, a migration checksum changed, or the schema is newer than the app.
A failed migration rolls back completely. Backups never overwrite an existing file.
