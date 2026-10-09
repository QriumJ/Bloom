using System;
using System.Collections.Generic;

namespace Bloom.App;

/// One answer in a form: the search line asks it, Enter moves on.
internal sealed class FormField
{
    public string Label = "";
    public string Hint = ""; // placeholder in the search line
    public string Value = "";
    public bool Optional;
    /// A shortcut pressed in rather than typed: the keys pressed with Ctrl / Alt / Shift / Win show as "Ctrl+Alt+M".
    public bool Keys;
    /// The cleaned value, or an error to show (and stay on this field).
    public Func<string, (string? value, string? error)>? Check;
}

/// A few questions asked one at a time in the launcher's search line (new / edit quicklink).
internal sealed class FormSpec
{
    public string Title = "";
    public readonly List<FormField> Fields = new();
    public int Step;
    public Mode Back = Mode.Root;
    public string? Error;
    /// Before a field is shown: fill it from the answers so far (only when it is still empty).
    public Action<FormSpec, int>? Prepare;
    public Action<FormSpec> Done = _ => { };

    public FormField Current => Fields[Math.Clamp(Step, 0, Fields.Count - 1)];

    public string this[int i] => Fields[i].Value;
}
