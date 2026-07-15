# Current PreProcess pipeline: reader used per file type

This is what the production ETL PreProcess pipeline uses today (from `EtlServiceFinder.cs:88-111`), not the comparison tool below.

| File type | Reader class | Underlying library/driver |
|---|---|---|
| `.xlsx` | `EtlExcelFileReader` | `ExcelDataReader` / `DocumentFormat.OpenXml` (managed, cross-platform) |
| `.mdb` / `.accdb` | `EtlAccessFileReader` | `System.Data.OleDb` — `Microsoft.ACE.OLEDB.12.0` (Windows-only) |
| `.txt` / `.csv` | `EtlCsvFileReader` | `CsvHelper` (managed, cross-platform) |
| USA country files (non-db) | `USDataFileReader` | Custom `FileStream`/text parsing (no OLEDB) |
| Format = `"db"` (any extension) | `EtlDbReader` | `Microsoft.Data.SqlClient` (cross-platform) |

`.mdb`/`.accdb` is the only file type still tied to a Windows-only driver — the reason this MMKiwi.MdbReader evaluation exists.

# MDB/ACCDB Reader: PreProcess Usage and OleDb vs MMKiwi.MdbReader Comparison
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

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader — Windows | MMKiwi.MdbReader — Linux |
|---|---|---|---|
| Tables found | 1 (`export`) | 1 (`export`) | 1 (`export`) |
| Rows read | 89,300 | 89,300 | 89,300 |
| Columns | 22 | 22 | 22 |
| Column types | String / Double | Text / Double | Text / Double |
| Elapsed time | 426 ms | 1,880 ms |  2539 ms |
| Errors | none | none | none |

Columns: `REG_DATE, EXPORTER_ID, EXPORTER, BUYER, DEST_COUNTRY, HS_CODE, QUANTITY, UNIT_OF_QUANTITY, PRODUCT_DESC, G_WEIGHT, N_WEIGHT, FOB, FREIGHT, INSURANCE, CIF, FOB_UNIT, BRAND, CUSTOMS, TRANS_TYPE, TRANS_CORP, TRANS_COUNTRY, ORDER1`

## Test 2 — `SampleMultiTable.mdb` (legacy Jet4 format — old `.mdb` engine, Access 97-2003, 3 tables) — 2026-07-15

Sample file created at `C:\mnt\SampleMultiTable.mdb` via ADOX/ACE OLEDB, bulk-inserted to 150,009 rows total across 3 tables (~6.6 MB file).

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader — Windows | MMKiwi.MdbReader — Linux (avg of 5 runs) |
|---|---|---|---|
| Tables found | 3 (`Customers`, `Orders`, `Products`) | 3 (`Customers`, `Orders`, `Products`) | 3 (`Customers`, `Orders`, `Products`) |
| Rows per table | Customers 50,003 (20 ms), Orders 50,002 (7 ms), Products 50,004 (7 ms) | Customers 50,003 (68 ms), Orders 50,002 (44 ms), Products 50,004 (51 ms) | Customers 50,003 (133 ms), Orders 50,002 (80 ms), Products 50,004 (201 ms) |
| Total elapsed | 167 ms | 185 ms | 504 ms |
| Errors | none | none | none |

## Test 3 — `UZ_202606_1.accdb` (Access2007, real production file, ~879 MB) — 2026-07-15

File path: `Z:\Anurag DCPL\UZBEKISTANEXPORT202606\UZ_202606_1.accdb`

| Metric | OleDb (ACE.OLEDB.12.0) — Windows | MMKiwi.MdbReader — Windows | MMKiwi.MdbReader — Linux |
|---|---|---|---|
| Tables found | 1 (`DATA`) | 1 (`DATA`) | 1 (`DATA`) |
| Rows read | 343,655 | 343,655 | 343,655 |
| Columns | 81 | 81 | 81 |
| Column types | String / Double / DateTime | Text / Memo / Double / DateTime | Text / Memo / Double / DateTime |
| Elapsed time | 70,706 ms | 152,340 ms | 35,755 ms |
| Errors | none | none | none |

Columns include Cyrillic names (e.g. `G1A_Направление перемещения`, `G31NAME_Наименование товара`). MMKiwi correctly identified 4 columns as `Memo` type (`G7A/G7B/G7C_Номер декларации`, `G31NAME_Наименование товара`, `G18NO_Номер трансп`, `G21NO_Номер транспорта на границе`) that OleDb reports as plain `String` — first test exercising the Memo field code path. Row/column counts match exactly between both readers, zero errors. MMKiwi is ~2.2x slower than OleDb at this scale.
