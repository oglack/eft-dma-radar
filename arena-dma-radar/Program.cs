global using eft_dma_shared;
global using eft_dma_shared.Common;
global using eft_dma_shared.Misc;
global using SDK;
global using SkiaSharp;
global using SkiaSharp.Views.Desktop;
global using System.Buffers;
global using System.Buffers.Binary;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.ComponentModel;
global using System.Data;
global using System.Diagnostics;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Numerics;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
using arena_dma_radar;
using arena_dma_radar.Arena;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.UI;
using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Windows;
using Application = System.Windows.Forms.Application;
using MessageBox = HandyControl.Controls.MessageBox;

[assembly: AssemblyTitle(Program.Name)]
[assembly: AssemblyProduct(Program.Name)]
[assembly: AssemblyCopyright("BSD Zero Clause License �2025 lone-dma")]
[assembly: AssemblyDescription("Advanced DMA radar for Escape from Tarkov: Arena")]
[assembly: AssemblyCompany("lone-dma")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: SupportedOSPlatform("Windows")]

namespace arena_dma_radar
{
    internal static class Program
    {
        internal const string Name = "Arena DMA Radar";
        internal const string Version = "1.0.0";

        /// <summary>
        /// Current application mode
        /// </summary>
        public static ApplicationMode CurrentMode { get; private set; } = ApplicationMode.Normal;

        /// <summary>
        /// Global Program Configuration.
        /// </summary>
        public static Config Config { get; private set; }

        /// <summary>
        /// Path to the Configuration Folder in %AppData%
        /// </summary>
        public static DirectoryInfo ConfigPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar"));

        /// <summary>
        /// Detailed version information from assembly
        /// </summary>
        public static class VersionInfo
        {
            private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

            public static Version AssemblyVersion => Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            public static string FileVersion => Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0.0";
            public static string InformationalVersion => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            public static string ProductVersion => InformationalVersion;
            public static string SimpleVersion => AssemblyVersion.ToString(3);
            public static string Company => Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";
            public static string Product => Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? Name;
            public static string Copyright => Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        }

        /// <summary>
        /// Update the global configuration reference (used for config imports)
        /// </summary>
        /// <param name="newConfig">The new configuration to use</param>
        public static void UpdateConfig(Config newConfig)
        {
            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            Config = newConfig;
            SharedProgram.UpdateConfig(Config);

            LoneLogging.WriteLine("[Program] Global config reference updated");
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static public void Main()
        {
            InitializeDpiAwareness();

            var app = new App();
            app.InitializeComponent();

            try
            {
                StartApplication(app, ApplicationMode.Normal);
            }
            catch (Exception ex)
            {
                HandleStartupException(app, ex);
            }
        }

        #region Private Members

        static Program()
        {
            try
            {
                ConfigPath.Create();
                var config = Config.Load();
                SharedProgram.Initialize(ConfigPath, config);
                Config = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                throw;
            }
        }

        /// <summary>
        /// Start the application in the specified mode
        /// </summary>
        private static void StartApplication(App app, ApplicationMode mode)
        {
            CurrentMode = mode;

            var mainWindow = new MainWindow();
            app.MainWindow = mainWindow;

            if (mode == ApplicationMode.Normal)
                ConfigureProgram();
            else
                ConfigureSafeMode();

            mainWindow.InitializeComponent();
            mainWindow.Show();
            mainWindow.Activate();

            //_ = Task.Run(async () => await CheckForUpdatesAsync(mainWindow));

            app.Run();
        }

        /// <summary>
        /// Check for application updates
        /// </summary>
        /// <param name="mainWindow">Main window reference for UI updates</param>
        private static async Task CheckForUpdatesAsync(MainWindow mainWindow)
        {
            try
            {
                await Task.Delay(2000);

                var result = await GitHubVersionChecker.CheckForUpdatesAsync(5000);

                if (result.IsSuccess && result.IsOutdated)
                {
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        ShowUpdateNotification(result);
                    });
                }
                else if (!result.IsSuccess)
                {
                    LoneLogging.WriteLine($"[Program] Version check failed: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Program] Error during version check: {ex.Message}");
            }
        }

        /// <summary>
        /// Show update notification to the user
        /// </summary>
        /// <param name="versionResult">Version check result</param>
        private static void ShowUpdateNotification(VersionCheckResult versionResult)
        {
            try
            {
                var message = $"A new version of {Name} is available!\n\n" +
                             $"Current Version: {versionResult.CurrentVersion}\n" +
                             $"Latest Version: {versionResult.LatestVersion}\n";

                if (!string.IsNullOrEmpty(versionResult.ReleaseNotes))
                {
                    var notes = versionResult.ReleaseNotes.Length > 200
                        ? versionResult.ReleaseNotes.Substring(0, 200) + "..."
                        : versionResult.ReleaseNotes;
                    message += $"\nWhat's New:\n{notes}\n";
                }

                message += "\nWould you like to visit the release page to download the update?";

                var result = MessageBox.Show(
                    message,
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var url = !string.IsNullOrEmpty(versionResult.ReleaseUrl)
                            ? versionResult.ReleaseUrl
                            : "https://github.com/lone-dma/arena-dma-radar/releases";

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[Program] Failed to open browser: {ex.Message}");

                        try
                        {
                            var url = !string.IsNullOrEmpty(versionResult.ReleaseUrl)
                                ? versionResult.ReleaseUrl
                                : "https://github.com/lone-dma/arena-dma-radar/releases";

                            System.Windows.Clipboard.SetText(url);
                            MessageBox.Show($"URL copied to clipboard:\n{url}",
                                          "URL Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch
                        {
                            MessageBox.Show("Please visit: https://github.com/lone-dma/arena-dma-radar/releases",
                                          "Manual Update", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Program] Error showing update notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle startup exceptions and offer recovery options
        /// </summary>
        private static void HandleStartupException(App app, Exception ex)
        {
            var errorMessage = ex.ToString();

            if (errorMessage.Contains("DMA Initialization Failed!"))
            {
                errorMessage += "\n\nWould you like to continue in Safe Mode? (UI and Config only)";

                var result = MessageBox.Show(errorMessage, "Continue in Safe Mode?", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    Environment.Exit(1);
            }
            else
            {
                var result = MessageBox.Show(
                    $"Startup Error: {ex.Message}\n\nWould you like to start in Safe Mode?",
                    "Startup Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    throw new Exception($"Application startup failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Configure the program for safe mode (no DMA functionality)
        /// </summary>
        private static void ConfigureSafeMode()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Starting in Safe Mode...", 10);
                LoneLogging.WriteLine("Starting application in Safe Mode - DMA functionality disabled");

                loading.UpdateStatus("Loading Configuration...", 25);

                loading.UpdateStatus("Initializing Safe Memory Interface...", 40);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Safe UI Components...", 50);
                try
                {
                    loading.UpdateStatus("Loading Tarkov.Dev Data...", 60);
                    EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                    loading.UpdateStatus("Caching Item Icons...", 70);
                    CacheAllItemIcons();
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"Non-critical safe mode component failed: {ex.Message}");
                }

                loading.UpdateStatus("Initializing Safe Mode Features...", 85);
                InitializeSafeModeFeatures();

                loading.UpdateStatus("Safe Mode Ready - DMA functions disabled", 100);
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Safe Mode initialization failed: {ex}");
                loading.UpdateStatus("Safe Mode initialization failed", 100);
                Thread.Sleep(1000);
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        /// <summary>
        /// Initialize features that are safe to use without DMA
        /// </summary>
        private static void InitializeSafeModeFeatures()
        {
            try
            {
                LoneLogging.WriteLine("Safe mode features initialized");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Error initializing safe mode features: {ex}");
            }
        }

        /// <summary>
        /// Configure Program Startup (Normal Mode).
        /// </summary>
        private static void ConfigureProgram()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Loading Tarkov.Dev Data...", 15);
                EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                loading.UpdateStatus("Caching Item Icons...", 25);
                CacheAllItemIcons();

                loading.UpdateStatus("Loading Map Assets...", 35);
                LoneMapManager.ModuleInit();

                loading.UpdateStatus("Starting DMA Connection...", 50);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Remaining Modules...", 75);
                FeatureManager.ModuleInit();

                ResourceJanitor.ModuleInit(new Action(CleanupWindowResources));

                loading.UpdateStatus("Loading Completed!", 100);
                Thread.Sleep(300);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        private static void CacheAllItemIcons()
        {
            string iconCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "Assets", "Icons", "Items");

            Directory.CreateDirectory(iconCachePath);

            Parallel.ForEach(EftDataManager.AllItems.Keys, itemId =>
            {
                try
                {
                    string pngPath = Path.Combine(iconCachePath, $"{itemId}.png");
                    if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 1024) return;

                    LoneLogging.WriteLine($"[IconCache] Caching item icon: {itemId}");
                    Converters.ItemIconConverter.SaveItemIconAsPng(itemId, iconCachePath).Wait();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IconCache] Failed to cache item {itemId}: {ex}");
                }
            });
        }

        private static void CleanupWindowResources()
        {
            MainWindow.Window?.PurgeSKResources();
            ESPForm.Window?.PurgeSKResources();
        }

        /// <summary>
        /// Initialize DPI awareness for the application
        /// </summary>
        private static void InitializeDpiAwareness()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

                LoneLogging.WriteLine("[DPI] Successfully enabled PerMonitorV2 DPI awareness");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DPI] Failed to set DPI awareness: {ex.Message}");

                try
                {
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    LoneLogging.WriteLine("[DPI] Fallback: Enabled SystemAware DPI awareness");
                }
                catch
                {
                    LoneLogging.WriteLine("[DPI] Warning: Could not enable DPI awareness");
                }
            }
        }

        #endregion

        #region GitHub Version Checker

        /// <summary>
        /// Enhanced version checker using GitHub Releases API
        /// </summary>
        private static class GitHubVersionChecker
        {
            private const string GITHUB_API_URL = "https://api.github.com/repos/lone-dma/arena-dma-radar/releases/latest";

            private static readonly HttpClient _httpClient = new HttpClient();
            private static DateTime _lastCheck = DateTime.MinValue;
            private static VersionCheckResult _cachedResult = null;
            private static readonly TimeSpan CacheTimeout = TimeSpan.FromMinutes(10);

            static GitHubVersionChecker()
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{Program.Name}/{GetCurrentVersionString()}");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
                _httpClient.Timeout = TimeSpan.FromSeconds(10);
            }

            /// <summary>
            /// Get current version string
            /// </summary>
            private static string GetCurrentVersionString()
            {
                return Program.Version;
            }

            /// <summary>
            /// Check for updates using GitHub Releases API
            /// </summary>
            public static async Task<VersionCheckResult> CheckForUpdatesAsync(int timeoutMs = 5000)
            {
                if (_cachedResult != null && DateTime.Now - _lastCheck < CacheTimeout)
                {
                    LoneLogging.WriteLine("[VersionChecker] Using cached result");
                    return _cachedResult;
                }

                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    var result = await CheckForUpdatesInternalAsync(cts.Token);

                    if (result.IsSuccess)
                    {
                        _cachedResult = result;
                        _lastCheck = DateTime.Now;
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    return new VersionCheckResult
                    {
                        IsOutdated = false,
                        Error = "Version check timed out",
                        CurrentVersion = GetCurrentVersionString()
                    };
                }
            }

            /// <summary>
            /// Internal version checking using GitHub Releases API
            /// </summary>
            private static async Task<VersionCheckResult> CheckForUpdatesInternalAsync(CancellationToken cancellationToken)
            {
                try
                {
                    LoneLogging.WriteLine("[VersionChecker] Checking for updates via GitHub Releases API...");

                    var latestRelease = await GetLatestReleaseAsync(cancellationToken);
                    if (latestRelease == null)
                    {
                        return new VersionCheckResult
                        {
                            IsOutdated = false,
                            Error = "Could not retrieve latest release from GitHub",
                            CurrentVersion = GetCurrentVersionString()
                        };
                    }

                    if (!TryParseVersion(GetCurrentVersionString(), out var currentVersion) ||
                        !TryParseVersion(latestRelease.TagName, out var latestVersion))
                    {
                        return new VersionCheckResult
                        {
                            IsOutdated = false,
                            Error = "Could not parse version numbers",
                            CurrentVersion = GetCurrentVersionString(),
                            LatestVersion = latestRelease.TagName
                        };
                    }

                    var isOutdated = currentVersion < latestVersion;

                    LoneLogging.WriteLine($"[VersionChecker] Current: {GetCurrentVersionString()}, Latest: {latestRelease.TagName}, Outdated: {isOutdated}");

                    return new VersionCheckResult
                    {
                        IsOutdated = isOutdated,
                        CurrentVersion = GetCurrentVersionString(),
                        LatestVersion = latestRelease.TagName,
                        ReleaseUrl = latestRelease.HtmlUrl,
                        ReleaseNotes = latestRelease.Body,
                        Error = null
                    };
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[VersionChecker] Error checking version: {ex.Message}");
                    return new VersionCheckResult
                    {
                        IsOutdated = false,
                        Error = ex.Message,
                        CurrentVersion = GetCurrentVersionString()
                    };
                }
            }

            /// <summary>
            /// Get latest release information from GitHub API
            /// </summary>
            private static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
            {
                try
                {
                    var response = await _httpClient.GetStringAsync(GITHUB_API_URL, cancellationToken);
                    var json = JsonNode.Parse(response);
                    var tagName = json?["tag_name"]?.ToString();
                    var name = json?["name"]?.ToString();
                    var htmlUrl = json?["html_url"]?.ToString();
                    var body = json?["body"]?.ToString();
                    var isDraft = json?["draft"]?.GetValue<bool>() ?? false;
                    var isPrerelease = json?["prerelease"]?.GetValue<bool>() ?? false;

                    if (string.IsNullOrEmpty(tagName))
                    {
                        LoneLogging.WriteLine("[VersionChecker] No tag_name found in GitHub API response");
                        return null;
                    }

                    if (isDraft)
                    {
                        LoneLogging.WriteLine("[VersionChecker] Latest release is a draft, skipping");
                        return null;
                    }

                    return new GitHubRelease
                    {
                        TagName = CleanVersionString(tagName),
                        Name = name ?? tagName,
                        HtmlUrl = htmlUrl ?? "",
                        Body = body ?? "",
                        IsPrerelease = isPrerelease
                    };
                }
                catch (HttpRequestException ex)
                {
                    LoneLogging.WriteLine($"[VersionChecker] HTTP error: {ex.Message}");
                    return null;
                }
                catch (JsonException ex)
                {
                    LoneLogging.WriteLine($"[VersionChecker] JSON parsing error: {ex.Message}");
                    return null;
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    LoneLogging.WriteLine("[VersionChecker] Request was cancelled");
                    return null;
                }
            }

            /// <summary>
            /// Clean version string by removing common prefixes like 'v'
            /// </summary>
            private static string CleanVersionString(string version)
            {
                if (string.IsNullOrWhiteSpace(version))
                    return version;

                version = version.Trim();
                if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    version = version.Substring(1);

                return version;
            }

            /// <summary>
            /// Robust version parsing that handles various formats
            /// </summary>
            private static bool TryParseVersion(string versionString, out Version version)
            {
                version = null;

                if (string.IsNullOrWhiteSpace(versionString))
                    return false;

                var cleanVersion = CleanVersionString(versionString);

                try
                {
                    version = new Version(cleanVersion);
                    return true;
                }
                catch (ArgumentException)
                {
                    var parts = cleanVersion.Split('-')[0].Split('.');
                    if (parts.Length >= 2)
                    {
                        try
                        {
                            var major = int.Parse(parts[0]);
                            var minor = int.Parse(parts[1]);
                            var build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                            var revision = parts.Length > 3 ? int.Parse(parts[3]) : 0;

                            version = new Version(major, minor, build, revision);
                            return true;
                        }
                        catch { }
                    }
                }
                catch { }

                return false;
            }

            /// <summary>
            /// Clear cache for manual refresh
            /// </summary>
            public static void ClearCache()
            {
                _cachedResult = null;
                _lastCheck = DateTime.MinValue;
                LoneLogging.WriteLine("[VersionChecker] Version check cache cleared");
            }

            /// <summary>
            /// GitHub release information
            /// </summary>
            private class GitHubRelease
            {
                public string TagName { get; set; }
                public string Name { get; set; }
                public string HtmlUrl { get; set; }
                public string Body { get; set; }
                public bool IsPrerelease { get; set; }
            }
        }

        /// <summary>
        /// Enhanced version check result with release information
        /// </summary>
        private class VersionCheckResult
        {
            /// <summary>
            /// Whether the current version is outdated
            /// </summary>
            public bool IsOutdated { get; set; }

            /// <summary>
            /// Current application version
            /// </summary>
            public string CurrentVersion { get; set; }

            /// <summary>
            /// Latest version available on GitHub
            /// </summary>
            public string LatestVersion { get; set; }

            /// <summary>
            /// URL to the release page
            /// </summary>
            public string ReleaseUrl { get; set; }

            /// <summary>
            /// Release notes/description
            /// </summary>
            public string ReleaseNotes { get; set; }

            /// <summary>
            /// Error message if version check failed
            /// </summary>
            public string Error { get; set; }

            /// <summary>
            /// Whether the version check was successful
            /// </summary>
            public bool IsSuccess => string.IsNullOrEmpty(Error);
        }

        #endregion
    }
}