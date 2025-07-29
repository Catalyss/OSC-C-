using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

public static class LogProcessor
{
    private static readonly string logDirectory = "C:\\Users\\Catalyss\\AppData\\LocalLow\\VRChat\\VRChat\\";
    private static readonly string logFilePattern = "output_log_*.txt";
    private static readonly string outputDir = "./logdata/";

    private static Dictionary<string, long> fileReadOffsets = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<LoggedInstance>>> categorizedLogs = new();
    private static readonly ConcurrentDictionary<string, int> templateCounts = new();
    private static readonly int repeatThreshold = 3;

    private static Thread monitorThread;

    private static volatile bool indexDirty = false;
    private static readonly object indexLock = new();
    private static readonly string indexFilePath = Path.Combine(outputDir, "index.json");
    private static ConcurrentDictionary<string, LogIndexEntry> logIndex = new();
    private static readonly string offsetFilePath = Path.Combine(outputDir, "offsets.json");

    private static void LoadOffsetsFromFile()
    {
        try
        {
            if (!File.Exists(offsetFilePath)) return;

            string json = File.ReadAllText(offsetFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, long>>(json);

            if (loaded != null)
            {
                fileReadOffsets = loaded;
                Console.WriteLine($"Loaded {loaded.Count} file offsets.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading file offsets: {ex.Message}");
        }
    }
    private static void SaveOffsetsToFile()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(fileReadOffsets, options);
            File.WriteAllText(offsetFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving file offsets: {ex.Message}");
        }
    }


    public static void Start()
    {
        LoadOffsetsFromFile();

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        LoadIndexFromFile();

        monitorThread = new Thread(ProcessLogFiles) { IsBackground = true };
        monitorThread.Start();

        Thread autoSaveThread = new Thread(AutoSaveIndexLoop) { IsBackground = true };
        autoSaveThread.Start();
    }

    private static void AutoSaveIndexLoop()
    {
        while (true)
        {
            Thread.Sleep(3000); // Save every 3 seconds if dirty

            if (indexDirty)
            {
                lock (indexLock)
                {
                    SaveIndexToFile();
                    SaveOffsetsToFile();
                    indexDirty = false;
                }
            }
        }
    }
    private static void SaveIndexToFile()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var sorted = logIndex
    .OrderByDescending(kv => kv.Value.Count)
    .ToDictionary(kv => kv.Key, kv => kv.Value);

            string json = JsonSerializer.Serialize(sorted, options);
            File.WriteAllText(indexFilePath, json);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving index: {ex.Message}");
        }
    }

    private static void LoadIndexFromFile()
    {
        try
        {
            if (!File.Exists(indexFilePath)) return;

            string json = File.ReadAllText(indexFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, LogIndexEntry>>(json);

            if (loaded != null)
            {
                foreach (var pair in loaded)
                    logIndex[pair.Key] = pair.Value;

                Console.WriteLine($"Loaded {loaded.Count} index entries from disk.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading index: {ex.Message}");
        }
    }
    private static string ExtractCategory(string log)
    {
        var match = Regex.Match(log, @"\[(\w+)\]");
        if (match.Success)
        {
            var category = match.Groups[1].Value;
            return Regex.IsMatch(category, @"^\d+$") ? "NUMBER ONLY" : category;
        }

        return "Uncategorized";
    }




    private static void ProcessLogFiles()
    {
        while (true)
        {
            string[] logFiles = Directory.GetFiles(logDirectory, logFilePattern)
                                         .OrderBy(f => f) // Optional: sort oldest to newest
                                         .ToArray();

            foreach (string filePath in logFiles)
            {
                if (!fileReadOffsets.ContainsKey(filePath))
                    fileReadOffsets[filePath] = 0;

                try
                {
                    using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.Seek(fileReadOffsets[filePath], SeekOrigin.Begin);

                    using StreamReader sr = new StreamReader(fs);
                    StringBuilder currentEntry = new();
                    string? line;
                    int currentLineNumber = 0;
                    int entryStartLine = -1;


                    while ((line = sr.ReadLine()) != null)
                    {
                        if (IsNewLogEntry(line))
                        {
                            if (currentEntry.Length > 0)
                            {
                                string fullLog = currentEntry.ToString().Trim();
                                ProcessFullLogEntry(fullLog, Path.GetFileName(filePath), entryStartLine);
                                currentEntry.Clear();
                            }
                            entryStartLine = currentLineNumber;
                        }

                        currentEntry.AppendLine(line);
                        currentLineNumber++;
                    }

                    // Final entry in file
                    if (currentEntry.Length > 0)
                    {
                        string fullLog = currentEntry.ToString().Trim();
                        ProcessFullLogEntry(fullLog, Path.GetFileName(filePath), entryStartLine);
                    }


                    fileReadOffsets[filePath] = fs.Position;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading {filePath}: {ex.Message}");
                }
            }

            Thread.Sleep(1000); // Adjust as needed
        }
    }
    private static void ProcessFullLogEntry(string fullLog, string sourceFileName, int lineNumber)
    {
        string category = ExtractCategory(fullLog);
        string template = NormalizeLine(fullLog);
        string templateHash = GetSafeHash(template);

        int count = templateCounts.AddOrUpdate(templateHash, 1, (_, c) => c + 1);

        if (count >= repeatThreshold)
        {
            lock (categorizedLogs)
            {
                if (!categorizedLogs.ContainsKey(category))
                    categorizedLogs[category] = new ConcurrentDictionary<string, List<LoggedInstance>>();

                if (!categorizedLogs[category].ContainsKey(templateHash))
                    categorizedLogs[category][templateHash] = new List<LoggedInstance>();

                var instance = new LoggedInstance
                {
                    FullLog = fullLog,
                    OriginalFile = sourceFileName,
                    LineNumber = lineNumber
                };

                categorizedLogs[category][templateHash].Add(instance);


                string categoryDir = Path.Combine(outputDir, category);
                if (!Directory.Exists(categoryDir))
                    Directory.CreateDirectory(categoryDir);

                string fileName = $"{templateHash}.txt";
                string fullPath = Path.Combine(categoryDir, fileName);
                File.AppendAllText(fullPath,
    $"[context:{sourceFileName}:{lineNumber}]\n{fullLog}\n------------------------\n");


                logIndex.AddOrUpdate(templateHash,
                    _ => new LogIndexEntry
                    {
                        Category = category,
                        Template = ExtractCoreLogInfo(fullLog),
                        Count = count,
                        LastSeen = DateTime.UtcNow,
                        File = Path.Combine(category, fileName),
                        OriginalFile = sourceFileName,
                        LineNumbers = new List<int> { lineNumber }
                    },
                    (_, existing) =>
                    {
                        existing.Template = ExtractCoreLogInfo(fullLog);
                        existing.Count = count;
                        existing.LastSeen = DateTime.UtcNow;
                        existing.OriginalFile = sourceFileName;

                        if (existing.LineNumbers == null)
                            existing.LineNumbers = new List<int>();

                        if (!existing.LineNumbers.Contains(lineNumber))
                            existing.LineNumbers.Add(lineNumber);

                        return existing;
                    });

                indexDirty = true;
            }
        }
    }

    private static string ExtractCoreLogInfo(string fullLog)
    {
        // Example: remove timestamp, bracketed metadata, or specific patterns
        // This will depend on your log format
        string noTimestamp = Regex.Replace(fullLog, @"^\[\d{2}:\d{2}:\d{2}\.\d{3}\]\s*", "");
        string noMemory = Regex.Replace(noTimestamp, @"0x[0-9a-fA-F]+", "0xXXXX");
        // Remove file paths, if cluttered
        string cleaned = Regex.Replace(noMemory, @"[A-Z]:\\[^\s]+", "<filepath>");
        // Maybe trim to first line if multiline
        cleaned = cleaned.Split('\n')[0];

        // Return concise cleaned log
        return cleaned.Trim();
    }

    private static bool IsNewLogEntry(string line)
    {
        return Regex.IsMatch(line, @"^\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}");
    }

    private static string NormalizeLine(string line)
    {
        // Remove timestamp (at start of line)
        line = Regex.Replace(line, @"^\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}", "");

        // Replace paths
        line = Regex.Replace(line, @"([A-Z]:)?(\\|\/)([\w\-\.\\\/]+)", "[PATH]");

        // Replace GUIDs or hashes (hex strings 6+ chars)
        line = Regex.Replace(line, @"\b[a-fA-F0-9]{6,}\b", "[HEX]");

        // Replace numbers (esp. timestamps or sizes)
        line = Regex.Replace(line, @"\b\d+\.\d+\b", "[FLOAT]");
        line = Regex.Replace(line, @"\b\d+\b", "[NUM]");

        // Remove extra whitespace
        return line.Trim();
    }


    private static string GetSafeHash(string input)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    public class LogIndexEntry
    {
        public string File { get; set; } = "";
        public string Category { get; set; } = "";
        public string Template { get; set; } = "";
        public int Count { get; set; }
        public DateTime LastSeen { get; set; }

        public string? OriginalFile { get; set; } = null;
        public List<int>? LineNumbers { get; set; } = null;
    }
    public class LoggedInstance
    {
        public string FullLog { get; set; } = "";
        public string OriginalFile { get; set; } = "";
        public int LineNumber { get; set; }
    }


}
