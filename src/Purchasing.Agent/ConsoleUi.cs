namespace Purchasing.Agent;

/// <summary>Konsola renkli yazı yazmak için küçük yardımcılar.</summary>
public static class ConsoleUi
{
    public static void Title(string text) => Write(ConsoleColor.Cyan, $"\n=== {text} ===");
    public static void Ok(string text) => Write(ConsoleColor.Green, text);
    public static void Warn(string text) => Write(ConsoleColor.Yellow, text);
    public static void Info(string text) => Write(ConsoleColor.Gray, text);

    private static void Write(ConsoleColor color, string text)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}
