namespace Mpbkpu.Utility;

public static class Console
{
    public static bool Confirm(string message, bool? defaultValue = null)
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
                if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape)
                    System.Console.WriteLine(key.KeyChar);
                else
                    System.Console.WriteLine(res == true ? "Y" : "N");

            }
        }

        return res.Value;
    }

    public static string? AskPassword()
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

        System.Console.WriteLine();
        return password;
    }
}