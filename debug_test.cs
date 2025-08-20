using System;

class Program
{
    static void Main()
    {
        long timestamp = 1640995200000L;
        DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
        string date = string.Format("{0:s}", dt);
        Console.WriteLine("Actual date format: '" + date + "'");
    }
}
