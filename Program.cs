using System.Diagnostics;

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

RunMdbTools(filePath, onlyTable);

return 0;

static void RunMdbTools(string filePath, string? onlyTable)
{
    Console.WriteLine("--- mdbtools (mdb-tables / mdb-export) ---");
    var sw = Stopwatch.StartNew();
    try
    {
        var tableNames = GetTableNames(filePath);
        Console.WriteLine($"Tables found: {tableNames.Count} -> {string.Join(", ", tableNames)}");

        foreach (var tableName in onlyTable != null ? new List<string> { onlyTable } : tableNames)
        {
            var tableSw = Stopwatch.StartNew();
            try
            {
                var (columns, rowCount) = ExportTable(filePath, tableName);
                tableSw.Stop();
                Console.WriteLine($"  Table [{tableName}]: {columns.Count} columns, {rowCount} rows, {tableSw.ElapsedMilliseconds} ms");
                Console.WriteLine($"    Columns: {string.Join(", ", columns)}");
            }
            catch (Exception tableEx)
            {
                tableSw.Stop();
                Console.WriteLine($"  Table [{tableName}] FAILED after {tableSw.ElapsedMilliseconds} ms: {tableEx.GetType().Name}: {tableEx.Message}");
            }
        }

        sw.Stop();
        Console.WriteLine($"mdbtools total time: {sw.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"mdbtools FAILED after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
    }
}

static List<string> GetTableNames(string filePath)
{
    var output = RunCommand("mdb-tables", "-1", filePath);
    return output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}

static (List<string> Columns, long RowCount) ExportTable(string filePath, string tableName)
{
    const char fieldDelimiter = '\x1f'; // ASCII Unit Separator: won't collide with real column data, unlike comma
    var output = RunCommand("mdb-export", "-d", fieldDelimiter.ToString(), filePath, tableName);
    var records = output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r').Split(fieldDelimiter).ToList())
        .ToList();

    if (records.Count == 0)
    {
        return ([], 0);
    }

    var columns = records[0];
    long rowCount = records.Count - 1;
    return (columns, rowCount);
}

static string RunCommand(string fileName, params string[] arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}: {stderr}");
    }

    return stdout;
}
