using System.Net;
using SteamBuildIdManager.Core.Models;
using SteamBuildIdManager.Core.Services;

// Dependency-free regression runner: dotnet run --project tests/SteamBuildIdManager.Core.Tests
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}

const string sampleAcf = """
"AppState"
{
	"appid"		"100"
	"universe"		"1"
	"LauncherPath"		"C:\\Steam\\steam.exe"
	"name"		"Test Game"
	"StateFlags"		"4"
	"installdir"		"TestGame"
	"SizeOnDisk"		"0"
	"buildid"		"111"
	"LastOwner"		"76561198000000000"
	"TargetBuildID"		"999"
	"InstalledDepots"
	{
		"101"
		{
			"manifest"		"201"
			"size"		"0"
		}
		"102"
		{
			"manifest"		"202"
			"size"		"0"
		}
	}
	"UserConfig"
	{
		"language"		"schinese"
	}
}
""";

var manifest = AcfDocument.Parse(@"C:\Library\steamapps\appmanifest_100.acf", @"C:\Library", sampleAcf);
Check(manifest.AppId == "100" && manifest.Name == "Test Game" && manifest.InstallDirectory == "TestGame",
    "ACF parser reads identity fields");
Check(manifest.BuildId == "111" && manifest.TargetBuildId == "999" && manifest.SizeOnDisk == 0,
    "ACF parser reads BuildID and placeholder size");
Check(manifest.DepotIds.SequenceEqual(new[] { "101", "102" }), "ACF parser reads InstalledDepots in order");
Check(manifest.LooksLikePlaceholder, "zero-size manifest is recognised as a placeholder configuration");

var updatedText = AcfDocument.WithBuildId(sampleAcf, "222");
var updated = AcfDocument.Parse(@"C:\Library\steamapps\appmanifest_100.acf", @"C:\Library", updatedText);
Check(updated.BuildId == "222" && updated.TargetBuildId == "0",
    "BuildID update replaces buildid and clears TargetBuildID");
Check(updatedText.Contains("\"LauncherPath\"\t\t\"C:\\\\Steam\\\\steam.exe\"")
    && updatedText.Contains("\"language\"\t\t\"schinese\"")
    && updatedText.Contains("\"manifest\"\t\t\"201\""),
    "BuildID update preserves launcher path, language and depot manifests");
Check(updatedText.Contains("\r\n") == sampleAcf.Contains("\r\n"), "BuildID update preserves newline style");

var noBuildId = sampleAcf.Replace("\t\"buildid\"\t\t\"111\"\r\n", "").Replace("\t\"buildid\"\t\t\"111\"\n", "");
var inserted = AcfDocument.Parse("appmanifest_100.acf", "", AcfDocument.WithBuildId(noBuildId, "333"));
Check(inserted.BuildId == "333", "missing buildid is inserted before the root closing brace");
Check(AcfDocument.ReadDepotIds("") .Count == 0, "missing InstalledDepots returns an empty list");

Check(BuildIdComparer.Compare("111", "222") == -1 && BuildIdComparer.Compare("222", "222") == 0
    && BuildIdComparer.Compare("333", "222") == 1, "BuildID comparison is numeric");
Check(BuildIdComparer.Compare("", "222") == -1 && BuildIdComparer.Compare("111", "abc") == null,
    "missing current BuildID can be updated and invalid latest is rejected");

const string json = """
{
  "status":"success", "data":{"123":{
    "common":{"name":"Example Game","name_localized":{"schinese":"示例游戏"}},
    "config":{"installdir":"Example Game","launch":{
      "0":{"executable":"Bin/Launcher.exe","description":"DirectX 11","arguments":"-dx11","config":{"oslist":"windows"}},
      "1":{"executable":"bin\\launcher.EXE","description":"DirectX 12","arguments":"-dx12","config":{"oslist":"windows"}},
      "2":{"executable":"linux/run.sh","config":{"oslist":"linux"}}
    }},
    "depots":{
      "branches":{"public":{"buildid":12345},"beta":{"buildid":"777"}},
      "124":{"name":"Windows content","config":{"oslist":"windows","osarch":"64"},
        "manifests":{"public":{"gid":18446744073709551615},"beta":"111"}},
      "125":{"config":{"oslist":"linux"},"manifests":{"public":"222"}},
      "126":{"dlcappid":"999","manifests":{"public":"333"}},
      "127":{"config":{"language":"japanese"},"manifests":{"public":"444"}}
    }
  }}
}
""";

var app = SteamAppInfoParser.Parse(json, "123");
var options = new SteamAppSelectionOptions();
var selection = SteamAppSelectionService.Select(app, options);
Check(selection.DisplayName == "示例游戏" && selection.InstallDirectory == "Example Game",
    "localized name does not replace install directory");
Check(selection.Depots.Count == 1 && selection.Depots[0].Depot.Id == "124",
    "platform/language/DLC filtering excludes inappropriate depots");
Check(selection.Depots[0].Manifest == "18446744073709551615" && selection.BuildId == "12345",
    "large numeric IDs preserve precision");
Check(selection.Executables.Count == 1 && selection.Executables[0].Path == @"Bin\Launcher.exe"
    && selection.Executables[0].LaunchOptions.Count == 2,
    "same EXE with different arguments is deduplicated and subdirectory retained");
Check(app.BranchBuildIds.Count == 2 && app.LaunchOptions.Count == 3,
    "raw model retains branch and launch metadata");
var beta = SteamAppSelectionService.Select(app, options with { Branch = "beta", Language = "english" });
Check(beta.BuildId == "777" && beta.Depots[0].Manifest == "111" && beta.DisplayName == "Example Game",
    "branch and language are configurable");

var commonFilter = new SteamPlatformFilter("", "", "");
var multi = app with
{
    Depots = app.Depots.Concat(new[] { new SteamDepotInfo("128", "Shared resources", commonFilter, "",
        new Dictionary<string, string> { ["public"] = "555" }) }).ToList(),
    LaunchOptions = app.LaunchOptions.Concat(new[] { new SteamLaunchInfo("3", "Setup", "Setup.exe", "", "", commonFilter) }).ToList()
};
var multiple = SteamAppSelectionService.Select(multi, options);
Check(multiple.Depots.Count == 2 && multiple.Warnings.Any(w => w.Contains("自动共同写入")),
    "multiple compatible depots are selected as one install set");
Check(multiple.Executables.Count == 2, "different EXEs remain separate candidates");
var bit32 = new SteamLaunchInfo("4", "32-bit", "Client32.exe", "", "", new("windows", "32", ""));
Check(SteamAppSelectionService.Select(app with { LaunchOptions = new[] { bit32 } }, options).Executables.Count == 1,
    "32-bit launchers remain usable on 64-bit Windows");

foreach (var path in new[] { @"C:\bad.exe", @"\bad.exe", @"..\bad.exe", "A/../bad.exe", "CON.exe",
             "A./bad.exe", "A /bad.exe", "A//bad.exe", "A\nbad.exe", "start.bat" })
    Check(!SteamPathValidator.TryValidate(path, out _, requireExe: true), "reject path " + path.Replace("\n", "\\n"));
Check(SteamPathValidator.TryValidate(@"子目录\Game Launcher.exe", out _, true),
    "relative EXE paths support spaces and Unicode");
Check(SteamAppInfoService.TryNormalizeAppId(" 000123 ", out var normalizedAppId) && normalizedAppId == "123",
    "AppID normalization");
foreach (var id in new[] { "0", "-1", "+1", "1.2", "4294967296", "abc" })
    Check(!SteamAppInfoService.TryNormalizeAppId(id, out _), "reject AppID " + id);

var sparse = SteamAppInfoParser.Parse(
    """{"status":"success","data":{"1":{"common":{"name":"Sparse"},"config":{"launch":[{"executable":"A.exe"}]}}}}""", "1");
Check(sparse.LaunchOptions.Count == 1 && sparse.Depots.Count == 0 && sparse.InstallDirectory == "",
    "array launch options and missing optional sections are supported");
try
{
    SteamAppInfoParser.Parse("""{"status":"success","data":{"123":{}}}""", "123");
    throw new Exception("FAILED: empty metadata accepted");
}
catch (SteamAppInfoException) { Check(true, "empty metadata produces a clear failure"); }

const string vdf = """
"libraryfolders"
{
	"0"
	{
		"path"		"C:\\Program Files (x86)\\Steam"
		"label"		""
		"apps"		{}
	}
	"1"
	{
		"path"		"D:\\SteamLibrary"
		"apps"		{}
	}
	"2"		"E:\\LegacyLibrary"
}
""";
var vdfPaths = SteamLocator.ParseLibraryFoldersVdf(vdf);
Check(vdfPaths.SequenceEqual(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"E:\LegacyLibrary" }),
    "libraryfolders.vdf supports modern path entries and legacy numeric entries");
Check(SteamLocator.NormalizeLibraryRoot(@"D:\SteamLibrary\steamapps") == @"D:\SteamLibrary",
    "library root normalization strips a trailing steamapps segment");

var acfText = AcfContentBuilder.Build(new AcfParameters(
    "123",
    new[]
    {
        new SteamDepotConfig { DepotId = "1", ManifestId = "11" },
        new SteamDepotConfig { DepotId = "2", ManifestId = "22", Size = "33" },
    },
    @"C:\Steam\steam.exe", "Game", "Game", "10", "76561198000000000"));
Check(acfText.Contains("\"buildid\"\t\t\"10\"") && acfText.Contains("\"StateFlags\"\t\t\"4\""),
    "ACF builder writes identity and installed state");
Check(acfText.Contains("\"manifest\"\t\t\"11\"") && acfText.Contains("\"manifest\"\t\t\"22\"")
    && acfText.IndexOf("\"1\"", StringComparison.Ordinal) < acfText.IndexOf("\"2\"", StringComparison.Ordinal),
    "ACF builder writes every depot in source order");
Check(acfText.Contains("\"LauncherPath\"\t\t\"C:\\\\Steam\\\\steam.exe\""),
    "ACF builder escapes the launcher path");
try
{
    AcfContentBuilder.Build(new AcfParameters("123", Array.Empty<SteamDepotConfig>(), "steam.exe",
        "Game", "Game", "10", "1"));
    throw new Exception("FAILED: empty depot set accepted");
}
catch (ArgumentException) { Check(true, "ACF builder rejects an empty depot set"); }

var root = Path.Combine(Path.GetTempPath(), "sbim-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var libraryRoot = Path.Combine(root, "library");
    var steamApps = Path.Combine(libraryRoot, "steamapps");
    Directory.CreateDirectory(steamApps);
    var acfPath = Path.Combine(steamApps, "appmanifest_100.acf");
    await File.WriteAllTextAsync(acfPath, sampleAcf);

    // Seed a second, unrelated manifest and a malformed file for scan coverage.
    await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_200.acf"),
        sampleAcf.Replace("\"appid\"\t\t\"100\"", "\"appid\"\t\t\"200\"")
            .Replace("\"name\"\t\t\"Test Game\"", "\"name\"\t\t\"Unmanaged Game\"")
            .Replace("\"SizeOnDisk\"\t\t\"0\"", "\"SizeOnDisk\"\t\t\"1024\"")
            .Replace("\"buildid\"\t\t\"111\"", "\"buildid\"\t\t\"555\""));
    await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_broken.acf"), "not an acf");

    var fake = new FakeBuildIdProvider(new Dictionary<string, string>
    {
        ["100"] = "222",
        ["200"] = "555",
    });
    var backups = Path.Combine(root, "backups");
    var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    var files = new ManifestFileService(backups, new ManifestBackupOptions(2, TimeSpan.FromDays(30)), () => now);
    var service = new BuildIdUpdateService(fake, files);
    var scan = service.Scan(new[] { libraryRoot });
    Check(scan.Manifests.Count == 2 && scan.Errors.Count == 1,
        "scan reads valid manifests and reports malformed ACF files");

    var checksList = await service.CheckAsync(scan.Manifests);
    var first = checksList.Single(r => r.Manifest.AppId == "100");
    var second = checksList.Single(r => r.Manifest.AppId == "200");
    Check(first.State == BuildIdState.UpdateAvailable && first.LatestBuildId == "222" && first.CanUpdate,
        "outdated placeholder manifest is flagged for update");
    Check(second.State == BuildIdState.UpToDate, "up-to-date manifest is not flagged");
    Check(fake.Calls[0] == "100" || fake.Calls[0] == "200", "provider is called for each manifest");

    var update = await service.UpdateAsync(first.Manifest, "222");
    Check(update.Write.Changed && update.Write.BackupPath != null && update.UpdatedManifest.BuildId == "222",
        "one-click update writes the new BuildID and creates a backup");
    Check(update.UpdatedManifest.TargetBuildId == "0", "one-click update clears TargetBuildID");
    Check(AcfDocument.ReadString(await File.ReadAllTextAsync(acfPath), "buildid") == "222",
        "updated BuildID is persisted to disk");

    var stale = first.Manifest with { BuildId = "999" };
    try
    {
        await service.UpdateAsync(stale, "333");
        throw new Exception("FAILED: stale manifest was overwritten");
    }
    catch (InvalidOperationException) { Check(true, "stale scan results are rejected instead of overwriting changes"); }

    var batch = await service.UpdateAllAsync(new[]
    {
        new BuildIdCheckResult(update.UpdatedManifest, "333", BuildIdState.UpdateAvailable, "test"),
        second with { LatestBuildId = "555", State = BuildIdState.UpToDate },
    });
    Check(batch.UpdatedCount == 1 && !batch.HasFailures, "batch update only touches outdated entries");

    var equalWrite = await files.WriteWithBackupAsync(acfPath, await File.ReadAllTextAsync(acfPath), "100", "333", "333");
    Check(!equalWrite.Changed && equalWrite.BackupPath == null, "identical ACF content creates no backup");

    for (var i = 0; i < 3; i++)
    {
        now = now.AddMinutes(1);
        await files.WriteWithBackupAsync(acfPath, sampleAcf.Replace("111", (400 + i).ToString()), "100", "333", "400");
    }
    Check(Directory.GetFiles(Path.Combine(backups, "100"), "*.acf").Length == 2,
        "backup retention keeps only the configured number per AppID");

    // ── GameConfigService end-to-end write ────────────────────────────────
    var steamRoot = Path.Combine(root, "steam");
    Directory.CreateDirectory(steamRoot);
    await File.WriteAllTextAsync(Path.Combine(steamRoot, "steam.exe"), "");
    var request = new GameConfigRequest(
        AppId: "100",
        DisplayName: "Test Game",
        InstallDirectory: "TestGame",
        PlaceholderExeName: "Game.exe",
        BuildId: "12345",
        SteamId: "76561198000000000",
        Depots: new[] { new SteamDepotConfig { DepotId = "101", ManifestId = "201" } },
        LibraryPath: libraryRoot,
        SteamInstallPath: steamRoot,
        RealGameExePath: @"D:\Games\TestGame\game.exe",
        LaunchArguments: "-dx11");
    Check(GameConfigService.Validate(request, out _, out _, out _) == null, "valid game config passes validation");
    Check(GameConfigService.Validate(request with { SteamId = "" }, out _, out _, out _)?.Contains("SteamID64") == true,
        "missing SteamID64 is rejected");
    Check(GameConfigService.Validate(request with { BuildId = "0" }, out _, out _, out _)?.Contains("BuildID") == true,
        "zero BuildID is rejected");
    Check(GameConfigService.Validate(request with { Depots = Array.Empty<SteamDepotConfig>() }, out _, out _, out _) != null,
        "empty depot set is rejected");

    var configService = new GameConfigService(files);
    var write = await configService.WriteAsync(request);
    Check(File.Exists(write.AcfPath) && File.Exists(write.PlaceholderExePath)
        && new FileInfo(write.PlaceholderExePath).Length == 0,
        "game config creates the ACF and an empty placeholder EXE");
    Check(write.LaunchCommand == "\"D:\\Games\\TestGame\\game.exe\" -dx11 %command%",
        "launch command points at the real game and keeps %command%");
    Check(AcfDocument.Parse(write.AcfPath, libraryRoot, await File.ReadAllTextAsync(write.AcfPath)).BuildId == "12345",
        "game config writes the requested BuildID");
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
}

// ── Settings round-trip ───────────────────────────────────────────────────
var settingsPath = Path.Combine(Path.GetTempPath(), "sbim-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
try
{
    var settings = new SettingsService(settingsPath);
    Check(settings.Save(new AppSettings
    {
        SteamInstallPath = @"C:\Steam",
        SteamLibraryPaths = { @"D:\Library" },
        SteamId = "76561198000000000",
        ShowAllSteamGames = true,
    }), "settings save succeeds");
    var loaded = settings.Load();
    Check(loaded.SteamInstallPath == @"C:\Steam" && loaded.SteamLibraryPaths.SequenceEqual(new[] { @"D:\Library" })
        && loaded.SteamId == "76561198000000000" && loaded.ShowAllSteamGames,
        "settings round-trip preserves all fields");

    File.WriteAllText(settingsPath, "{ not valid json");
    Check(settings.Load().SteamLibraryPaths.Count == 0, "invalid settings JSON falls back to defaults");
}
finally
{
    try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, recursive: true); } catch { }
}

// ── HTTP transport and cancellation ─────────────────────────────────────
var calls = 0;
using var http = new HttpClient(new StubHandler((request, _) =>
{
    calls++;
    Check(request.RequestUri == new Uri("https://example.test/info/123"), "injectable endpoint and normalized request");
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
}));
var service2 = new SteamAppInfoService(http, new Uri("https://example.test/info/"));
var fetched = await service2.GetAppInfoAsync("000123");
Check(calls == 1 && fetched.Name == "Example Game" && fetched.Depots.Count == 4,
    "one HTTP request supplies all metadata");

using var cancellation = new CancellationTokenSource();
using var waitingClient = new HttpClient(new StubHandler(async (_, token) =>
{
    await Task.Delay(Timeout.Infinite, token);
    return new HttpResponseMessage(HttpStatusCode.OK);
}));
var pending = new SteamAppInfoService(waitingClient).GetAppInfoAsync("123", cancellation.Token);
cancellation.Cancel();
try
{
    await pending;
    throw new Exception("FAILED: cancellation ignored");
}
catch (OperationCanceledException) { Check(true, "caller cancellation propagates"); }

var providerApp = await new SteamBuildIdProvider(new StubAppInfoService("888")).GetLatestBuildIdAsync("123");
Check(providerApp == "888", "BuildID provider reads the public branch");
var cachedProvider = new SteamBuildIdProvider(new CountingAppInfoService("888"));
Check(await cachedProvider.GetLatestBuildIdAsync("123") == "888"
    && await cachedProvider.GetLatestBuildIdAsync("123") == "888", "BuildID provider returns values");
Check(await cachedProvider.GetLatestBuildIdAsync("123", forceRefresh: true) == "888",
    "BuildID provider supports forced refresh");

if (args.Contains("--live"))
{
    var live = await new SteamAppInfoService().GetAppInfoAsync("4162040");
    var result = SteamAppSelectionService.Select(live, options);
    Check(result.Depots.Count > 0 && result.Executables.Count > 0 && result.BuildId != "",
        "live API provides usable metadata");
    Console.WriteLine($"Live: {result.DisplayName} | {result.InstallDirectory} | ID {result.BuildId}");
}

Console.WriteLine($"All {checks} checks passed.");

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => handler(request, cancellationToken);
}

sealed class FakeBuildIdProvider(IReadOnlyDictionary<string, string> values) : IBuildIdProvider
{
    public List<string> Calls { get; } = new();

    public Task<string?> GetLatestBuildIdAsync(string appId, bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        lock (Calls) Calls.Add(appId);
        return Task.FromResult(values.TryGetValue(appId, out var value) ? value : null);
    }
}

sealed class StubAppInfoService(string buildId) : ISteamAppInfoService
{
    public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SteamAppInfo(appId, "Game", new Dictionary<string, string>(), "Game",
            new Dictionary<string, string> { ["public"] = buildId },
            Array.Empty<SteamDepotInfo>(), Array.Empty<SteamLaunchInfo>()));
}

sealed class CountingAppInfoService(string buildId) : ISteamAppInfoService
{
    public int Calls { get; private set; }

    public Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new SteamAppInfo(appId, "Game", new Dictionary<string, string>(), "Game",
            new Dictionary<string, string> { ["public"] = buildId },
            Array.Empty<SteamDepotInfo>(), Array.Empty<SteamLaunchInfo>()));
    }
}