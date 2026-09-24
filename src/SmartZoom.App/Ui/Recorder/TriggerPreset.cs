using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Recorder;

/// <summary>One of the ready-made triggers the recorder offers as a button.</summary>
/// <param name="Label">What the button says, in the words a user would use for the input.</param>
/// <param name="Trigger">The trigger it applies, with its own tap and swallow policy.</param>
internal sealed record TriggerPreset(string Label, TriggerSettings Trigger);
