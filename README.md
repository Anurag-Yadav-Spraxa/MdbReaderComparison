# Part 1: Current production ETL PreProcess pipeline

Background context: the readers currently used by production, per file type.

| File type | Reader class | Underlying library/driver |
|---|---|---|
| `.xlsx` | `EtlExcelFileReader` | `ExcelDataReader` / `DocumentFormat.OpenXml` (managed, cross-platform) |
| `.mdb` / `.accdb` | `EtlAccessFileReader` | `System.Data.OleDb` — `Microsoft.ACE.OLEDB.12.0` (Windows-only) |
| `.txt` / `.csv` | `EtlCsvFileReader` | `CsvHelper` (managed, cross-platform) |
| USA country files (non-db) | `USDataFileReader` | Custom `FileStream`/text parsing (no OLEDB) |
| Format = `"db"` (any extension) | `EtlDbReader` | `Microsoft.Data.SqlClient` (cross-platform) |

`.mdb`/`.accdb` is the only file type still tied to a Windows-only driver — the reason the MMKiwi.MdbReader evaluation in Part 2 below exists.

# Part 2: MDB/ACCDB Reader Comparison — OleDb vs MMKiwi.MdbReader vs mdbtools

A standalone benchmarking tool (separate from the production pipeline above) used to evaluate MMKiwi.MdbReader as a possible cross-platform replacement for the Windows-only OleDb reader.

## Project location

Created new console application at `c:\mnt\IIPL\MdbReaderComparison\` (project file: `MdbReaderComparison.csproj`)

## How the console application was created

```
cd c:\mnt\IIPL\MdbReaderComparison
dotnet new console -n MdbReaderComparison -o .
```

## Added packages

```
dotnet add package System.Data.OleDb
dotnet add package MMKiwi.MdbReader --prerelease 
```

Then `Program.cs` was written to read the file path from `args[0]`, run the OleDb reader, then the MMKiwi.MdbReader reader, against the same file, and print columns/types, row count, elapsed time, and errors for each.

## How to run

```
cd c:\mnt\IIPL\MdbReaderComparison
dotnet run -- <path-to-mdb-or-accdb> [tableName]
```

Runs OleDb, then MMKiwi.MdbReader, against the same file. Reports per table: columns/types, row count, elapsed time, errors. `[tableName]` optionally limits to one table.

## Test 1 — `202606-EXP-RAW.accdb` (Access2007, single table, ~54 MB) — 2026-07-15

Tables found: 1 (`export`). Rows read: 89,300. Columns: 22.

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader (0.1.0-beta1) — Windows | MMKiwi.MdbReader (0.1.0-beta1) — Linux | MMKiwi.MdbReader (zgabi + wj-hcs forks) — Windows Release avg | mdbtools — Linux Release avg (5 runs) |
|---|---|---|---|---|---|
| Column types | String / Double | Text / Double | Text / Double | Text / Double | (untyped — CSV export) |
| Elapsed time | 426 ms | 1,880 ms |  2539 ms | 1,860.7 ms | 1,161 ms |
| Errors | none | none | none | none | none |

## Test 2 — `SampleMultiTable.mdb` (legacy Jet4 format — old `.mdb` engine, Access 97-2003, 3 tables) — 2026-07-15

Sample file `SampleMultiTable.mdb` created via ADOX/ACE OLEDB, bulk-inserted to 150,009 rows total across 3 tables (~6.6 MB file).

Tables found: 3 (`Customers`, `Orders`, `Products`).

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader — Windows | MMKiwi.MdbReader — Linux (avg of 5 runs) | MMKiwi.MdbReader (zgabi + wj-hcs forks) — Windows Release avg | mdbtools — Linux Release avg (5 runs) |
|---|---|---|---|---|---|
| Rows per table | Customers 50,003 (20 ms), Orders 50,002 (7 ms), Products 50,004 (7 ms) | Customers 50,003 (68 ms), Orders 50,002 (44 ms), Products 50,004 (51 ms) | Customers 50,003 (133 ms), Orders 50,002 (80 ms), Products 50,004 (201 ms) | Customers 50,003 (102 ms), Orders 50,002 (47.5 ms), Products 50,004 (62.75 ms) | Customers 50,003 (57.2 ms), Orders 50,002 (102.6 ms), Products 50,004 (101 ms) |
| Total elapsed | 167 ms | 185 ms | 504 ms | 212.25 ms | 283.2 ms |
| Errors | none | none | none | none | none |

## Test 3 — `UZ_202606_1.accdb` (Access2007, real production file, ~879 MB) — 2026-07-15

Tables found: 1 (`DATA`). Rows read: 343,655. Columns: 81.

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader (0.1.0-beta1) — Windows | MMKiwi.MdbReader (0.1.0-beta1) — Linux | MMKiwi.MdbReader (zgabi + wj-hcs forks) — Windows Release avg | mdbtools — Linux Release avg (4 runs) |
|---|---|---|---|---|---|
| Column types | String / Double / DateTime | Text / Memo (long text) / Double / DateTime | Text / Memo (long text) / Double / DateTime | Text / Memo (long text) / Double / DateTime | (untyped — CSV export) |
| Elapsed time | 70,706 ms | 152,340 ms | 35,755 ms | 8,066.75 ms | 20,193.25 ms |
| Errors | none | none | none | none | none |

## Note on mdbtools integration

mdbtools required noticeably more custom code than the two managed readers: OleDb and MMKiwi.MdbReader are called directly as libraries, but mdbtools is a CLI tool (`mdb-tables` / `mdb-export`), so `Program.cs` has to shell out via `Process.Start`, capture stdout, and parse the output itself. `mdb-export`'s default CSV output doesn't quote the header row, so a column name containing commas would get mis-split — this was fixed by running `mdb-export`/`mdb-tables` with a non-comma field delimiter (ASCII Unit Separator `\x1f`) instead of parsing CSV.

Example: `UZ_202606_1.accdb`'s `G54NAME_Фамилия,  имя и отчество,  а также адрес электронной почты` column name contains two literal commas. In the unquoted CSV header row, a plain comma split turned this single column into three, inflating the reported column count from 81 to 83 for that table.
