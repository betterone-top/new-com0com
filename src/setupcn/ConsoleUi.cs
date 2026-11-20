using Com0com.Core;

namespace Com0com.Cli;

/// <summary>Console front-end: traces to stdout (and optionally a log file), asks on the console.</summary>
internal sealed class ConsoleUi : ISetupUi
{
    public bool Silent { get; set; }
    public bool Quiet { get; set; }
    public string? OutputFile { get; set; }

    public void Write(string text)
    {
        if (!Quiet) Console.Out.Write(text);
        Console.Out.Flush();
        if (OutputFile != null)
        {
            try { File.AppendAllText(OutputFile, text.ReplaceLineEndings("\r\n")); }
            catch (IOException) { }
        }
    }

    /// <summary>Plain output that is never suppressed by <see cref="Quiet"/>.</summary>
    public void Print(string text)
    {
        Console.Out.WriteLine(text);
        if (OutputFile != null)
        {
            try { File.AppendAllText(OutputFile, text + "\r\n"); }
            catch (IOException) { }
        }
    }

    public Choice Ask(string message, Buttons buttons, bool warning = false)
    {
        if (Silent || Console.IsInputRedirected) return Choice.None;

        (string Label, char Key, Choice Choice)[] options = buttons switch
        {
            Buttons.Ok => [("OK", 'o', Choice.Ok)],
            Buttons.OkCancel => [("OK", 'o', Choice.Ok), ("Cancel", 'c', Choice.Cancel)],
            Buttons.YesNo => [("Yes", 'y', Choice.Yes), ("No", 'n', Choice.No)],
            Buttons.RetryCancel => [("Retry", 'r', Choice.Retry), ("Cancel", 'c', Choice.Cancel)],
            _ => [("Cancel", 'c', Choice.Cancel), ("Try again", 't', Choice.TryAgain), ("Continue", 'o', Choice.Continue)],
        };
        if (options.Length == 1)
        {
            // Informational message: already traced, nothing to choose.
            return Choice.Ok;
        }

        string prompt = string.Join(" / ", options.Select(o => $"[{o.Key}] {o.Label}"));
        var color = Console.ForegroundColor;
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write($"\n{prompt}: ");
            Console.ForegroundColor = color;
            var line = Console.ReadLine();
            if (line == null) return Choice.None;
            line = line.Trim().ToLowerInvariant();
            foreach (var o in options)
            {
                if (line.Length > 0 && (line[0] == o.Key || o.Label.Equals(line, StringComparison.OrdinalIgnoreCase)))
                    return o.Choice;
            }
        }
    }
}
