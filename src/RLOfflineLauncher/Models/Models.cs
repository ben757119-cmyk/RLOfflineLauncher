namespace RLOfflineLauncher.Models;

public sealed class CosmeticItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Rarity { get; init; }
    public List<string> Tags { get; init; } = new();
    public string PreviewColor { get; init; } = "#808080";
    public string PreviewGlyph { get; init; } = "?";
    public string IntegrationKey { get; init; } = string.Empty;
}

public sealed class CosmeticsCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public List<CosmeticItem> Items { get; init; } = new();
}

public enum EacDetectionState
{
    NotScanned,
    NotDetected,
    Detected,
    Inconclusive
}

public sealed class EacScanResult
{
    public EacDetectionState State { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int ProcessesScanned { get; init; }
    public int ProcessesInaccessible { get; init; }
    public IReadOnlyList<string> MatchedProcessNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> MatchedProcessIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public string Summary => State switch
    {
        EacDetectionState.NotDetected => "Clear – no Easy Anti-Cheat processes detected",
        EacDetectionState.Detected => $"Detected – matched: {string.Join(", ", MatchedProcessNames)}",
        EacDetectionState.Inconclusive => "Inconclusive – safety check could not be completed reliably",
        _ => "Not scanned"
    };
}

public enum LaunchState
{
    Idle,
    Scanning,
    BlockedEacDetected,
    BlockedScanInconclusive,
    BlockedUnsupportedMechanism,
    BlockedInvalidPath,
    Launching,
    ProcessStarted,
    Failed
}

public enum IntegrationStatus
{
    NotInitialized,
    PreviewOnly,
    Ready,
    AppliedLocally,
    Error
}

public sealed class ApplyResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IntegrationStatus NewStatus { get; init; }

    public static ApplyResult PreviewOnly(string itemName) => new()
    {
        Success = true,
        Message = $"Previewed locally: '{itemName}'. No game integration is active; nothing changed in the game.",
        NewStatus = IntegrationStatus.AppliedLocally
    };

    public static ApplyResult Reset(string? itemName) => new()
    {
        Success = true,
        Message = itemName is null
            ? "All local previews cleared. No game state was modified."
            : $"Local preview of '{itemName}' cleared. No game state was modified.",
        NewStatus = IntegrationStatus.PreviewOnly
    };

    public static ApplyResult Failed(string reason) => new()
    {
        Success = false,
        Message = reason,
        NewStatus = IntegrationStatus.Error
    };
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string GamePath { get; set; } = string.Empty;
    public bool UseOfflineFlag { get; set; } = true;
    public bool UseNoAntiCheatFlag { get; set; } = true;
    public string HotkeyModifier { get; set; } = "F3";
    public string HotkeyKey { get; set; } = "T";
    public bool AllowHotkeyWhenLauncherFocused { get; set; }
    public string LogLevel { get; set; } = "Information";
}

public enum LaunchMechanismKind
{
    DirectExecutable,
    SteamUri,
    EpicUri,
    Unknown
}

public sealed class LaunchPlan
{
    public LaunchMechanismKind Mechanism { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public bool ArgumentsSupported { get; init; }
    public string CapabilityNote { get; init; } = string.Empty;
    public bool IsSupported => Mechanism != LaunchMechanismKind.Unknown && ArgumentsSupported;
}
