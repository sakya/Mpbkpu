using System.Text;

namespace Mpbkpu.Utility;

public static class Console
{
    public const ConsoleColor DefaultColor = ConsoleColor.Gray;
    public const ConsoleColor HighlightColor = ConsoleColor.White;

    public const ConsoleColor DebugColor = ConsoleColor.DarkGray;
    public const ConsoleColor InfoColor = ConsoleColor.Blue;
    public const ConsoleColor SuccessColor = ConsoleColor.Green;
    public const ConsoleColor WarningColor = ConsoleColor.Yellow;
    public const ConsoleColor ErrorColor = ConsoleColor.Red;

    /// <summary>
    /// False when the output is redirected (scripts, scheduled tasks, pipes): no cursor, no progress lines
    /// </summary>
    public static bool IsInteractive => !System.Console.IsOutputRedirected;

    public static void SetCursorVisible(bool visible)
    {
        // The cursor can be hidden only on Windows and only with a console
        if (IsInteractive && OperatingSystem.IsWindows())
            System.Console.CursorVisible = visible;
    }

    /// <summary>
    /// Overwrite the current line with a progress text (nothing when the output is redirected)
    /// </summary>
    public static void WriteProgress(string text)
    {
        if (!IsInteractive)
            return;
        var width = System.Console.WindowWidth - 1;
        System.Console.CursorLeft = 0;
        System.Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
    }

    /// <summary>
    /// A path shortened from the start to fit in the given width
    /// </summary>
    public static string FitPath(string path, int usedWidth)
    {
        if (!IsInteractive)
            return path;
        var available = System.Console.WindowWidth - usedWidth - 1;
        return path.Length > available && available > 3 ? $"...{path[^(available - 3)..]}" : path;
    }

    public static bool Confirm(string message, bool? defaultValue = null, bool clearLine = false)
    {
        if (defaultValue == true)
            message = $"{message} [Y/n] ";
        else if (defaultValue == false)
            message = $"{message} [y/N] ";
        else
            message = $"{message} [y/n] ";

        System.Console.Write(message);
        if (System.Console.IsInputRedirected) {
            // No keyboard: read an answer line, the default when there is none
            var line = System.Console.ReadLine()?.Trim();
            var answer = string.IsNullOrEmpty(line) ? defaultValue ?? false : line.StartsWith('y') || line.StartsWith('Y');
            System.Console.WriteLine(answer ? "Y" : "N");
            return answer;
        }
        bool? res = null;
        while (res == null) {
            var key = System.Console.ReadKey(true);
            switch (key.Key) {
                case ConsoleKey.Y:
                    res = true;
                    break;
                case ConsoleKey.N:
                    res = false;
                    break;
                case ConsoleKey.Enter:
                    if (defaultValue.HasValue)
                        res = defaultValue.Value;
                    break;
                case ConsoleKey.Escape:
                    res = false;
                    break;
            }

            if (res.HasValue) {
                if (clearLine) {
                    ClearLine();
                } else {
                    if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape)
                        System.Console.WriteLine(key.KeyChar);
                    else
                        System.Console.WriteLine(res == true ? "Y" : "N");
                }
            }
        }

        return res.Value;
    }

    public static string? AskPassword(bool clearLine = false)
    {
        // No keyboard: the password is the first input line
        if (System.Console.IsInputRedirected) {
            System.Console.WriteLine();
            return System.Console.ReadLine();
        }

        string? password = null;
        bool done = false;
        while (!done) {
            var key = System.Console.ReadKey(true);
            switch (key.Key) {
                case ConsoleKey.Enter:
                    done = true;
                    break;
                case ConsoleKey.Backspace:
                    if (password?.Length > 0)
                        password = password.Remove(password.Length - 1, 1);
                    break;
                default:
                    if (key.KeyChar != '\0')
                        password = $"{password}{key.KeyChar}";
                    break;
            }
        }

        if (clearLine)
            ClearLine();
        else
            System.Console.WriteLine();
        return password;
    }

    /// <summary>
    /// Clear the current line
    /// </summary>
    public static void ClearLine()
    {
        if (!IsInteractive)
            return;
        var sb = new StringBuilder();
        for (var i = 0; i < System.Console.WindowWidth; i++)
            sb.Append("\b \b");
        System.Console.CursorLeft = System.Console.WindowWidth - 1;
        System.Console.Write(sb.ToString());
        System.Console.CursorLeft = 0;
    }
}