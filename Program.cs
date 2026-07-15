using System.Data.OleDb;
using System.Diagnostics;
using MMKiwi.MdbReader;
using MMKiwi.MdbReader.Values;

if (args.Length == 0)
{
    Console.WriteLine("Usage: MdbReaderComparison <path-to-mdb-or-accdb> [tableName]");
    return 1;
}

string filePath = args[0];
string? onlyTable = args.Length > 1 ? args[1] : null;

if (!File.Exists(filePath))
{
    Console.WriteLine($"File not found: {filePath}");
    return 1;
}

Console.WriteLine($"Comparing readers for: {filePath}");
Console.WriteLine(new string('=', 80));

RunOleDb(filePath, onlyTable);
Console.WriteLine();
RunMmkiwi(filePath, onlyTable);

return 0;

static void RunOleDb(string filePath, string? onlyTable)
{
    Console.WriteLine("--- OleDb (Microsoft.ACE.OLEDB.12.0) ---");
    var sw = Stopwatch.StartNew();
    try
    {
        string connectionString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};Persist Security Info=False;";
        using var connection = new OleDbConnection(connectionString);
        connection.Open();

        var schemaTable = connection.GetSchema("Tables", new string?[] { null, null, null, "TABLE" });
        var tableNames = new List<string>();
        foreach (System.Data.DataRow row in schemaTable.Rows)
        {
            tableNames.Add(row["TABLE_NAME"].ToString() ?? "");
        }

        Console.WriteLine($"Tables found: {tableNames.Count} -> {string.Join(", ", tableNames)}");

        foreach (var tableName in onlyTable != null ? new List<string> { onlyTable } : tableNames)
        {
            var tableSw = Stopwatch.StartNew();
            using var command = new OleDbCommand($"SELECT * FROM [{tableName}]", connection);
            using var reader = command.ExecuteReader();

            var columnNames = new List<string>();
            var columnTypes = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames.Add(reader.GetName(i));
                columnTypes.Add(reader.GetFieldType(i).Name);
            }

            long rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
            }

            tableSw.Stop();
            Console.WriteLine($"  Table [{tableName}]: {reader.FieldCount} columns, {rowCount} rows, {tableSw.ElapsedMilliseconds} ms");
            Console.WriteLine($"    Columns: {string.Join(", ", columnNames.Zip(columnTypes, (n, t) => $"{n}:{t}"))}");
        }

        sw.Stop();
        Console.WriteLine($"OleDb total time: {sw.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"OleDb FAILED after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
    }
}

void RunMmkiwi(string filePath, string? onlyTable)
{
    Console.WriteLine("--- MMKiwi.MdbReader ---");
    var sw = Stopwatch.StartNew();
    try
    {
        using MdbConnection handle = MdbConnection.Open(filePath);
        var tables = handle.Tables;

        Console.WriteLine($"Jet version: {handle.JetVersion}, Encoding: {handle.Encoding?.CodePage}");
        Console.WriteLine($"Tables found: {tables.Count} -> {string.Join(", ", tables.Keys)}");

        foreach (var tableName in onlyTable != null ? new List<string> { onlyTable } : tables.Keys.ToList())
        {
            var tableSw = Stopwatch.StartNew();
            try
            {
                var table = tables[tableName];
                var columnInfo = table.Columns.Select(c => $"{c.Name}:{c.Type}");

                long rowCount = 0;
                foreach (var row in table.Rows)
                {
                    for (int i = 0; i < row.FieldCount; i++)
                    {
                        try
                        {
                            _ = row.GetColumnType(i) switch
                            {
                                MdbColumnType.OLE => ReadOle(row.GetStream(i)),
                                MdbColumnType.Memo => ReadMemo(row.GetStreamReader(i)),
                                MdbColumnType.Binary => row.IsNull(i) ? null : Convert.ToBase64String(row.GetBytes(i)),
                                _ => row.GetValue(i)
                            };
                        }
                        catch (Exception fieldEx)
                        {
                            Console.WriteLine($"    [warn] row {rowCount} col {i} ({row.GetName(i)}): {fieldEx.GetType().Name}: {fieldEx.Message}");
                        }
                    }
                    rowCount++;
                }

                tableSw.Stop();
                Console.WriteLine($"  Table [{tableName}]: {table.Columns.Length} columns, {rowCount} rows, {tableSw.ElapsedMilliseconds} ms");
                Console.WriteLine($"    Columns: {string.Join(", ", columnInfo)}");
            }
            catch (Exception tableEx)
            {
                tableSw.Stop();
                Console.WriteLine($"  Table [{tableName}] FAILED after {tableSw.ElapsedMilliseconds} ms: {tableEx.GetType().Name}: {tableEx.Message}");
            }
        }

        sw.Stop();
        Console.WriteLine($"MMKiwi.MdbReader total time: {sw.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"MMKiwi.MdbReader FAILED after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
    }
}

string? ReadOle(MdbLValStream fOle)
{
    using (fOle)
    {
        return Convert.ToBase64String(fOle.ReadToEnd());
    }
}

string? ReadMemo(StreamReader? fMemo)
{
    if (fMemo == null) return null;
    using (fMemo)
    {
        return fMemo.ReadToEnd();
    }
}
