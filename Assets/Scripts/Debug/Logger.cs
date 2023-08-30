using System;
using System.Globalization;
using System.IO;
using PokerLogs;
using UnityEngine;
using Application = UnityEngine.Application;

// ReSharper disable UnusedMember.Local

public static class Logger
{
    private static readonly string PokerLogReaderFilePath;

    private static DateTime DateTime => DateTime.Now;
    private static RuntimePlatform Platform => Application.platform;

    static Logger()
    {
        #if UNITY_EDITOR
        return;
        #endif
        
        PokerLogReaderFilePath = $"{Application.persistentDataPath}\\Log_{DateTime.UtcNow.ToString(CultureInfo.CurrentCulture).ReplaceAll(new[] {' ', '.', ':', '\\', '/'}, '_')}.plr";
        
        if (File.Exists(PokerLogReaderFilePath) == false)
        {
            File.Create(PokerLogReaderFilePath).Close();
        }
        else
        {
            File.WriteAllText(PokerLogReaderFilePath, $"App Version: {Application.version}. Runtime platform: {Platform.ToString()}.\n\r");
        }
    }

    public static void Log(object message, LogLevel logLevel = LogLevel.Info)
    {
#if !UNITY_EDITOR

        LogMessage logMessage = new(DateTime, message, logLevel);
        WriteToFile(logMessage, logLevel);

#endif

        switch (logLevel)
        {
            case LogLevel.Info:
                Debug.Log(message);
                break;
            case LogLevel.Warning:
                Debug.LogWarning(message);
                break;
            case LogLevel.Error:
                Debug.LogError(message);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }
    }

    private static void WriteToFile(LogMessage message, LogLevel logLevel)
    {
        try
        {
            using StreamWriter sw = new(PokerLogReaderFilePath, true);
            sw.WriteLine(message.ToString());
        }
        catch (Exception e)
        {
            Debug.LogError($"{nameof(e)} {e.Message}");
        }
    }

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
    }
}