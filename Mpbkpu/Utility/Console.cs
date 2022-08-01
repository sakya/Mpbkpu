namespace Mpbkpu.Utility;

public static class Console
{
    public static bool Confirm(string message)
    {
        System.Console.Write($"{message} [Y/N] ");
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
            }
            if (res.HasValue)
                System.Console.WriteLine(key.KeyChar);
        }

        return res.Value;
    }

    public static void ShowProgressbar(int length, double perc)
    {
        System.Console.CursorLeft = 0;

    }
}