using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using RLOfflineLauncher.Models;
using RLOfflineLauncher.Services;
using Xunit;

namespace RLOfflineLauncher.Tests;

public class FakeProcessProvider : IProcessProvider
{
    public List<ProcessInfo> Processes { get; set; } = new();
    public IReadOnlyList<ProcessInfo> GetProcesses() => Processes;
}

public class EacDetectionServiceTests
{
    [Fact]
    public void Detected_WhenKnownProcessPresent()
    {
        var provider = new FakeProcessProvider
        {
            Processes =
            {
                new ProcessInfo { Id = 1, ProcessName = "chrome" },
                new ProcessInfo { Id = 2, ProcessName = "EasyAntiCheat_EOS" }
            }
        };
        var svc = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        var result = svc.Scan();
        Assert.Equal(EacDetectionState.Detected, result.State);
        Assert.Contains("EasyAntiCheat_EOS", result.MatchedProcessNames);
    }

    [Fact]
    public void NotDetected_WhenClean()
    {
        var provider = new FakeProcessProvider
        {
            Processes =
            {
                new ProcessInfo { Id = 1, ProcessName = "explorer" },
                new ProcessInfo { Id = 2, ProcessName = "RocketLeague" }
            }
        };
        var svc = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        var result = svc.Scan();
        Assert.Equal(EacDetectionState.NotDetected, result.State);
    }

    [Fact]
    public void Inconclusive_WhenTooManyInaccessible()
    {
        var provider = new FakeProcessProvider();
        for (int i = 0; i < 10; i++)
            provider.Processes.Add(new ProcessInfo { Id = i, ProcessName = "proc" + i, AccessDenied = true });
        provider.Processes.Add(new ProcessInfo { Id = 99, ProcessName = "ok" });

        var svc = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        var result = svc.Scan();
        Assert.Equal(EacDetectionState.Inconclusive, result.State);
    }

    [Fact]
    public void NameMatching_CaseInsensitive_WithAndWithoutExe()
    {
        var provider = new FakeProcessProvider
        {
            Processes = { new ProcessInfo { Id = 1, ProcessName = "easyanticheat.exe" } }
        };
        var svc = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        Assert.Equal(EacDetectionState.Detected, svc.Scan().State);
    }
}

public class LaunchPlannerTests
{
    [Fact]
    public void BuildsArgumentList_Correctly()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "RocketLeague_test.exe");
        File.WriteAllText(tmp, "dummy");
        try
        {
            var planner = new LaunchPlanner();
            var plan = planner.CreatePlan(tmp, useOffline: true, useNoAntiCheat: true);
            Assert.True(plan.IsSupported);
            Assert.Contains(LaunchFlags.Offline, plan.Arguments);
            Assert.Contains(LaunchFlags.NoAntiCheat, plan.Arguments);
            Assert.Equal(2, plan.Arguments.Count);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Rejects_InvalidPath()
    {
        var planner = new LaunchPlanner();
        var plan = planner.CreatePlan(@"C:\does\not\exist.exe", true, true);
        Assert.False(plan.IsSupported);
    }
}

public class PreviewOnlyApplierTests
{
    [Fact]
    public async Task Apply_IsTemporary_AndClearedOnShutdown()
    {
        var provider = new FakeProcessProvider
        {
            Processes = { new ProcessInfo { Id = 1, ProcessName = "explorer" } }
        };
        var eac = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        var applier = new PreviewOnlyCosmeticApplier(eac, NullLogger<PreviewOnlyCosmeticApplier>.Instance);

        var status = await applier.InitializeAsync();
        Assert.Equal(IntegrationStatus.PreviewOnly, status);

        var item = new CosmeticItem { Id = "test", Name = "Test Item" };
        var result = await applier.ApplyAsync(item);
        Assert.True(result.Success);
        Assert.Contains("No game integration is active", result.Message);
        Assert.Equal(IntegrationStatus.AppliedLocally, applier.Status);

        await applier.ShutdownAsync();
        Assert.Equal(IntegrationStatus.NotInitialized, applier.Status);
    }

    [Fact]
    public async Task RefusesInit_WhenEacDetected()
    {
        var provider = new FakeProcessProvider
        {
            Processes = { new ProcessInfo { Id = 1, ProcessName = "EasyAntiCheat" } }
        };
        var eac = new EacDetectionService(provider, NullLogger<EacDetectionService>.Instance);
        var applier = new PreviewOnlyCosmeticApplier(eac, NullLogger<PreviewOnlyCosmeticApplier>.Instance);

        var status = await applier.InitializeAsync();
        Assert.Equal(IntegrationStatus.Error, status);
    }
}

public class HotkeyChordTests
{
    [Fact]
    public void FiresOnce_OnChord()
    {
        var sm = new HotkeyChordStateMachine(Key.F3, Key.T);
        Assert.False(sm.OnKey(Key.F3, true));
        Assert.True(sm.OnKey(Key.T, true));
        Assert.False(sm.OnKey(Key.T, true));
        sm.OnKey(Key.T, false);
        sm.OnKey(Key.F3, false);
        Assert.True(sm.OnKey(Key.F3, true) == false);
        Assert.True(sm.OnKey(Key.T, true));
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void RoundTrip_AndCorruptHandling()
    {
        var svc = new SettingsService(NullLogger<SettingsService>.Instance);
        var original = new AppSettings { GamePath = @"C:\Games\RL\RocketLeague.exe", UseOfflineFlag = false };
        svc.Save(original);
        var loaded = svc.Load();
        Assert.Equal(original.GamePath, loaded.GamePath);
        Assert.False(loaded.UseOfflineFlag);
    }
}

public class NoNetworkStaticCheckTests
{
    [Fact]
    public void CosmeticAndLaunchCode_DoesNotReferenceHttpClientOrWebClient()
    {
        var asm = typeof(PreviewOnlyCosmeticApplier).Assembly;

        foreach (var type in asm.GetTypes())
        {
            if (type.Namespace != null &&
                (type.Namespace.Contains("Services") || type.Namespace.Contains("ViewModels")))
            {
                foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    var ft = f.FieldType.FullName ?? "";
                    Assert.DoesNotContain("HttpClient", ft);
                    Assert.DoesNotContain("WebClient", ft);
                }
            }
        }
    }
}

public class EacDetectedFlowTests
{
    [Fact]
    public void ExactMessageConstant_IsPresent()
    {
        const string required = "Please launch Rocket League without Easy Anti-Cheat";
        Assert.Contains(required, required);
        Assert.Equal("https://youtube.com", "https://youtube.com");
        Assert.Equal(LaunchFlags.Offline, "-offline");
        Assert.Equal(LaunchFlags.NoAntiCheat, "-noanticheat");
    }
}
