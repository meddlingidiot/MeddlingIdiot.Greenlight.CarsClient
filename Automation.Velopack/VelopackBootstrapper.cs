using System.Runtime.CompilerServices;
using Velopack;
using Velopack.Sources;

[assembly: InternalsVisibleTo("Automation.Velopack.UnitTests")]

namespace Automation.Velopack;

public static class VelopackBootstrapper
{
    internal static readonly string SharedDeptFolder = "Automation";

    internal static string ResolveChannel(string appName)
    {
        // 1) Env var override (CI/testing friendly)
        var env = Environment.GetEnvironmentVariable("VELOPACK_CHANNEL");
        Log(appName,$"ENV VELOPACK_CHANNEL: {env}");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        // 2) Command line (allow a shortcut like: MyApp.exe --channel=Prerelease)
        var args = Environment.GetCommandLineArgs().Skip(1);
        foreach (var arg in args)
        {
            if (arg.StartsWith("--channel=", StringComparison.OrdinalIgnoreCase))
            {
                Log(appName,$"Command line arg channel: {arg}");
                return arg.Split('=')[1];
            }
        }

        // 3) Marker file (user opt-in): %LOCALAPPDATA%\YourApp\.channel (contents: Stable/Prerelease).
        var globalChannelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SharedDeptFolder, ".channel");
        if (File.Exists(globalChannelFile))
        {
            var ch = File.ReadAllText(globalChannelFile).Trim();
            Log(appName,$"Global channel file: {globalChannelFile}, content: {ch}");
            if (!string.IsNullOrEmpty(ch)) return ch;
        }

        // 4) Marker file (user opt-in): %LOCALAPPDATA%\YourApp\.channel (contents: Stable/Prerelease)
        var applicationChannelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName, ".channel");
        if (File.Exists(applicationChannelFile))
        {
            var ch = File.ReadAllText(applicationChannelFile).Trim();
            Log(appName,$"Application channel file: {applicationChannelFile}, content: {ch}");
            if (!string.IsNullOrEmpty(ch)) return ch;
        }

        // default
        return "Stable";
    }

    /// <summary>
    /// Where MeddlingIdiot applications publish their releases, and therefore where they must
    /// look for updates.
    /// </summary>
    /// <remarks>
    /// Replaced AFTR's staftrinstallers, which this package pointed at from the Nuke-era
    /// default. Every MeddlingIdiot build overrides the *upload* target
    /// (IHasVelopack.AzureBlobAccount) but nothing overrode the runtime check, so applications
    /// uploaded to one container and asked another for updates -- answering 404 forever, and
    /// only visible once an update was actually published.
    ///
    /// Read-and-list only. A SAS embedded in a shipped desktop application is readable by
    /// anyone who has the application, so it must not be able to write or delete: that would
    /// hand every user the ability to replace the installers everyone else downloads.
    /// Expires 2029-12-31.
    /// </remarks>
    private const string DefaultSourceUrl =
        "https://meddlingidiotinstallers.blob.core.windows.net/installers/"
        + "?sp=rl&st=2025-12-28T02:30:01Z&se=2029-12-31T10:45:01Z&spr=https&sv=2024-11-04"
        + "&sr=c&sig=gnclHWB2peK1jLBP1Mf9yhuiCqgmH6Tv5qusYtgVADc%3D";

    /// <summary>
    /// Where to look for updates, in order of precedence: the VELOPACK_SOURCE_URL environment
    /// variable, then whatever the caller passed, then the MeddlingIdiot feed.
    /// </summary>
    /// <remarks>
    /// The environment variable comes first deliberately. An application shipped pointing at
    /// the wrong container cannot update itself to a build that points at the right one, so
    /// without an override outside the binary the only fix is a reinstall.
    /// </remarks>
    internal static string ResolveSourceUrl(string? explicitUrl)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("VELOPACK_SOURCE_URL");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return string.IsNullOrWhiteSpace(explicitUrl) ? DefaultSourceUrl : explicitUrl!;
    }

    /// <param name="sourceUrl">
    /// Overrides the feed for an application that does not publish to the MeddlingIdiot
    /// container. Rarely needed: the default now matches where the builds actually upload.
    /// </param>
    public static void Startup(
        string applicationName, 
        string[]? args = null,
        Action<int>? progress = null,
        string? sourceUrl = null)
    {
        string logStep = "Application.Startup() begin";
        // This does not work from here. must be called directly from startup.
        //VelopackApp.Build().Run();
        try
        {
            // Check for --skip-update flag
            args ??= Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Any(a => a.Equals("--skip-update", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // Check if version is pinned
            if (IsVersionPinned(applicationName))
            {
                return;
            }

            var channel = ResolveChannel(applicationName); // "Stable" or "Prerelease"

            logStep = "Create Instance UpdateManager()";
            var resolvedUrl = ResolveSourceUrl(sourceUrl);
            var source = new SimpleWebSource(resolvedUrl);
            var checkOptions = new UpdateOptions
            {
                ExplicitChannel = $"{applicationName}-{channel}-win"
            };
            Log(applicationName,$"Source URL: {resolvedUrl} Channel: {checkOptions.ExplicitChannel}");
            var mgr = new UpdateManager(source, checkOptions);

            // check for new version
            logStep = "Check for new version";
            var newVersion = mgr.CheckForUpdatesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            logStep = $"newVersion = {newVersion}";
            
            Log(applicationName,logStep);
            
            if (newVersion != null)
            {
                // download new version
                logStep = "Download Updates...";
                Log(applicationName,logStep);

                // Invoked to expose start of download (with -1) and end of download (with 101)
                progress?.Invoke(-1);
                mgr.DownloadUpdatesAsync(
                    newVersion,
                    progress)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                progress?.Invoke(101);

                // install new version and restart app
                logStep = "Apply Updates and Restart...";
                Log(applicationName,logStep);
                var restartArgs = args.Where(a => !a.Equals("--skip-update", StringComparison.OrdinalIgnoreCase)).ToArray();
                mgr.ApplyUpdatesAndRestart(newVersion, restartArgs);
            }
        }
        catch (Exception ex)
        {
            Log(applicationName,$"Step: {logStep} Exception: {ex.Message} {Environment.NewLine} StackTrace: {ex.StackTrace}");
        }
    }

    private static void Log(string applicationName, string message)
    {
        try
        {
            if (!Directory.Exists("C:\\Temp"))
            {
                Directory.CreateDirectory("C:\\Temp");
            }

            File.AppendAllText($"C:\\Temp\\{applicationName}-Log.txt", $"{message}\r\n");
        }
        catch
        {
            // Intentionally left blank.
        }

        Console.WriteLine(message);
    }

    public static void DeleteChannelFile(ChannelScope scope, string? appName = null)
    {
        appName ??= string.Empty;
        var pathScope = scope == ChannelScope.Global ? SharedDeptFolder : appName;
        var channelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pathScope, ".channel");
        if (File.Exists(channelFile)) File.Delete(channelFile);
    }

 
    public static bool CreateChannelFile(ChannelScope scope, string appName, string channel = "Prerelease")
    {
        if (channel != "Stable" && channel != "Prerelease")
            throw new ArgumentException("Channel must be either 'Stable' or 'Prerelease'.", nameof(channel));

        var pathScope = scope == ChannelScope.Global ? SharedDeptFolder : appName;
        var channelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pathScope, ".channel");
        var containingFolder = Path.GetDirectoryName(channelFile);
        if (containingFolder == null)
            return false;

        if (!Directory.Exists(containingFolder))
            Directory.CreateDirectory(containingFolder);
        if (File.Exists(channelFile)) File.Delete(channelFile);
        File.WriteAllText(channelFile, channel);
        return true;
    }

    public static string? ReadChannelFile(ChannelScope scope, string appName)
    {
        var pathScope = scope == ChannelScope.Global ? SharedDeptFolder : appName;
        var channelFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pathScope, ".channel");

        if (File.Exists(channelFile))
        {
            var ch = File.ReadAllText(channelFile).Trim();
            if (!string.IsNullOrEmpty(ch)) return ch;
        }

        return null;
    }

    public static bool IsVersionPinned(string appName)
    {
        // Check application-level pin file first
        var applicationPinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName, ".pinned");
        if (File.Exists(applicationPinFile))
        {
            var content = File.ReadAllText(applicationPinFile).Trim();
            return content.Equals("true", StringComparison.OrdinalIgnoreCase) || content == "1";
        }

        // Check global pin file
        var globalPinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SharedDeptFolder, ".pinned");
        if (File.Exists(globalPinFile))
        {
            var content = File.ReadAllText(globalPinFile).Trim();
            return content.Equals("true", StringComparison.OrdinalIgnoreCase) || content == "1";
        }

        return false;
    }

    public static bool CreatePinFile(ChannelScope scope, string appName, bool pinned = true)
    {
        var pathScope = scope == ChannelScope.Global ? SharedDeptFolder : appName;
        var pinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pathScope, ".pinned");
        var containingFolder = Path.GetDirectoryName(pinFile);
        if (containingFolder == null)
            return false;

        if (!Directory.Exists(containingFolder))
            Directory.CreateDirectory(containingFolder);

        if (pinned)
        {
            File.WriteAllText(pinFile, "true");
        }
        else if (File.Exists(pinFile))
        {
            File.Delete(pinFile);
        }

        return true;
    }

    public static bool? ReadPinFile(ChannelScope scope, string appName)
    {
        var pathScope = scope == ChannelScope.Global ? SharedDeptFolder : appName;
        var pinFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            pathScope, ".pinned");

        if (File.Exists(pinFile))
        {
            var content = File.ReadAllText(pinFile).Trim();
            return content.Equals("true", StringComparison.OrdinalIgnoreCase) || content == "1";
        }

        return null;
    }
}