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

    public static bool Confirm(string message, bool? defaultValue = null, bool clearLine = false)
    {
        if (defaultValue == true)
            message = $"{message} [Y/n] ";
        else if (defaultValue == false)
            message = $"{message} [y/N] ";
        else
            message = $"{message} [y/n] ";

        System.Console.Write(message);
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
        var sb = new StringBuilder();
        for (var i = 0; i < System.Console.WindowWidth; i++)
            sb.Append("\b \b");
        System.Console.CursorLeft = System.Console.WindowWidth - 1;
        System.Console.Write(sb.ToString());
        System.Console.CursorLeft = 0;
    }
}