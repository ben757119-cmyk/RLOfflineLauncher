using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using RLOfflineLauncher.Interop;

namespace RLOfflineLauncher.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler? ChordPressed;
    bool IsInstalled { get; }
    string LastError { get; }
    void Start();
    void Stop();
    void UpdateChord(Key modifier, Key key);
}

/// <summary>
/// Low-level keyboard hook that detects a two-key chord (default F3 + T).
/// Semantics: both keys must be down at the same time; edge-triggered so holding does not spam.
/// Only evaluates the configured chord – no keylogging of other keys.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private Key _modifier = Key.F3;
    private Key _key = Key.T;
    private bool _modifierDown;
    private bool _keyDown;
    private bool _firedThisPress;

    public event EventHandler? ChordPressed;
    public bool IsInstalled => _hookId != IntPtr.Zero;
    public string LastError { get; private set; } = string.Empty;

    public HotkeyService(ILogger<HotkeyService> logger) => _logger = logger;

    public void UpdateChord(Key modifier, Key key)
    {
        _modifier = modifier;
        _key = key;
        _logger.LogInformation("Hotkey chord updated to {Mod} + {Key}", modifier, key);
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule?.ModuleName is null)
        {
            LastError = "Could not obtain module handle";
            return;
        }

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            LastError = $"SetWindowsHookEx failed (Win32 error {err})";
            _logger.LogError(LastError);
        }
        else
        {
            LastError = string.Empty;
            _logger.LogInformation("Low-level keyboard hook installed for {Mod}+{Key}", _modifier, _key);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _logger.LogInformation("Keyboard hook uninstalled");
        }
        _proc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var key = KeyInterop.KeyFromVirtualKey((int)info.vkCode);

                if (key == _modifier)
                {
                    _modifierDown = isDown;
                    if (isUp) _firedThisPress = false;
                }
                else if (key == _key)
                {
                    _keyDown = isDown;
                    if (isUp) _firedThisPress = false;
                }

                if (_modifierDown && _keyDown && !_firedThisPress)
                {
                    _firedThisPress = true;
                    // Dispatch to UI thread later via event; keep callback fast
                    try { ChordPressed?.Invoke(this, EventArgs.Empty); }
                    catch { /* never throw from hook */ }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Pure logic helper for unit tests of the chord state machine.
/// </summary>
public sealed class HotkeyChordStateMachine
{
    private readonly Key _modifier;
    private readonly Key _key;
    private bool _modDown;
    private bool _keyDown;
    private bool _fired;

    public HotkeyChordStateMachine(Key modifier, Key key)
    {
        _modifier = modifier;
        _key = key;
    }

    public bool OnKey(Key key, bool isDown)
    {
        if (key == _modifier)
        {
            _modDown = isDown;
            if (!isDown) _fired = false;
        }
        else if (key == _key)
        {
            _keyDown = isDown;
            if (!isDown) _fired = false;
        }

        if (_modDown && _keyDown && !_fired)
        {
            _fired = true;
            return true;
        }
        return false;
    }
}
