using Serilog;

namespace StreamFlow.Core.Helpers;
public static class LoggerService
{
    private static Serilog.Core.Logger Log { get; } = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Debug()
        .CreateLogger();

    private static string FormatMessage(string callingtype, string message) => $"[{callingtype}] {message}";
    public static void InfoLog(Type caller, string message) => InfoLog(caller.Name, message);
    public static void WarnLog(Type caller, string message) => WarnLog(caller.Name, message);
    public static void ErrorLog(Type caller, string message) => ErrorLog(caller.Name, message);
    public static void DebugLog(Type caller, string message) => DebugLog(caller.Name, message);
    public static void FatalLog(Type caller, string message) => FatalLog(caller.Name, message);
    public static void VerboseLog(Type caller, string message) => VerboseLog(caller.Name, message);

    public static void InfoLog(string callername, string message) => Log.Information(FormatMessage(callername, message));
    public static void WarnLog(string callername, string message) => Log.Warning(FormatMessage(callername, message));
    public static void ErrorLog(string callername, string message) => Log.Error(FormatMessage(callername, message));
    public static void DebugLog(string callername, string message)
    {
#if DEBUG
        Log.Debug(FormatMessage(callername, message));
#endif
    }
    public static void FatalLog(string callername, string message) => Log.Fatal(FormatMessage(callername, message));
    public static void VerboseLog(string callername, string message) => Log.Verbose(FormatMessage(callername, message));
}
