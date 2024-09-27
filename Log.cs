using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

public static class Log
{
    static Log()
    {
        Initialize("[{Timestamp:HH:mm:ss} {Level:u3}] {Message}{NewLine}{Exception}", false);
    }

    public static ILogger Instance => Serilog.Log.Logger;

    public static void Initialize(string template, bool debug)
    {
        var theme = new SystemConsoleTheme(
            new Dictionary<ConsoleThemeStyle, SystemConsoleThemeStyle>
            {
                [ConsoleThemeStyle.Invalid] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.Yellow },
                [ConsoleThemeStyle.Null] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.Name] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.String] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.Number] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.Boolean] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.Scalar] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.White },
                [ConsoleThemeStyle.LevelVerbose] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.DarkGray },
                [ConsoleThemeStyle.LevelDebug] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.DarkGray },
                [ConsoleThemeStyle.LevelInformation] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.Blue },
                [ConsoleThemeStyle.LevelWarning] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.Black, Background = ConsoleColor.Yellow },
                [ConsoleThemeStyle.LevelError] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.Black, Background = ConsoleColor.Red },
                [ConsoleThemeStyle.LevelFatal] = new SystemConsoleThemeStyle { Foreground = ConsoleColor.Black, Background = ConsoleColor.Red },
            }
        );

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(debug ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.Console(
                outputTemplate: template,
                theme: theme
            );

        Serilog.Log.Logger = logger.CreateLogger();
    }

    public static void Disable()
    {
        Serilog.Log.Logger = Logger.None;
    }

    public static void Cleanup()
    {
        Serilog.Log.CloseAndFlush();
    }

    public static async Task TryDebug(Func<Task<object?>> messageFunc)
    {
        if (!Serilog.Log.Logger.IsEnabled(LogEventLevel.Debug))
        {
            return;
        }

        if (await messageFunc() is object message)
        {
            Debug(message);
        }
    }

    public static void TryDebug(Func<object?> messageFunc)
    {
        if (!Serilog.Log.Logger.IsEnabled(LogEventLevel.Debug))
        {
            return;
        }

        if (messageFunc() is object message)
        {
            Debug(message);
        }
    }

    public static void Debug(object message)
    {
        Serilog.Log.Logger.Debug(message.ToString() ?? "");
    }

    public static void Info(object message)
    {
        Serilog.Log.Logger.Information(message.ToString() ?? "");
    }

    public static void Warn(object message)
    {
        Serilog.Log.Logger.Warning(message.ToString() ?? "");
    }

    public static void Warn(object message, Exception e)
    {
        Serilog.Log.Logger.Warning(e, message.ToString() ?? "");
    }

    public static void Error(object message)
    {
        Serilog.Log.Logger.Error(message.ToString() ?? "");
    }

    public static void Error(object message, Exception e)
    {
        Serilog.Log.Logger.Error(e, message.ToString() ?? "");
    }
}