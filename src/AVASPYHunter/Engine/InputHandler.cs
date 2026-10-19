using Avalonia.Input;

namespace AVASPYHunter.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: steering, speed (up to go faster, down to brake),
/// the guns and the special weapon.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);

    public void Update() => _pad.Update();

    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private static readonly Key[] LeftKeys = [Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] RightKeys = [Key.Right, Key.D, Key.NumPad6];
    private static readonly Key[] UpKeys = [Key.Up, Key.W, Key.NumPad8];
    private static readonly Key[] DownKeys = [Key.Down, Key.S, Key.NumPad2];

    public Controls Controls
    {
        get
        {
            int dx = Axis(LeftKeys, RightKeys), dy = Axis(UpKeys, DownKeys);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            if (dy == 0) dy = _pad.DPadUp || _pad.LeftStickY < -0.4f ? -1 : _pad.DPadDown || _pad.LeftStickY > 0.4f ? 1 : 0;
            bool fire = Has(Key.Z) || Has(Key.Space) || Has(Key.LeftCtrl) || Has(Key.RightCtrl) || _pad.ButtonA;
            bool special = Has(Key.X) || Has(Key.LeftShift) || Has(Key.RightShift) || _pad.ButtonB || _pad.ButtonY;
            return new Controls(dx, dy, fire, special);
        }
    }

    public bool Start => Has(Key.Enter) || Has(Key.Space) || _pad.ButtonStart || _pad.ButtonA;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
