using System.Threading.Channels;

namespace SmartZoom.Core.Input;

/// <summary>Produces trigger events. The Windows implementation is a pair of low-level mouse and keyboard hooks.</summary>
public interface ITriggerSource
{
    /// <summary>Triggers in the order they were detected. Never completes while the source is running.</summary>
    ChannelReader<TriggerEvent> Triggers { get; }

    /// <summary>When false, all input passes through untouched and no triggers are raised.</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Replaces the gestures being watched for, without interrupting capture. Any press being held for a
    /// possible second tap is released to the application first.
    /// </summary>
    /// <param name="triggers">The new set. Buttons and key combinations must be distinct.</param>
    /// <exception cref="ArgumentException">No triggers, or two triggers share an input.</exception>
    void SetTriggers(IEnumerable<TriggerDefinition> triggers);

    /// <summary>Begins capturing input. Throws if capture cannot be installed.</summary>
    void StartCapture();

    /// <summary>Stops capturing input and releases any held (swallowed) presses.</summary>
    void StopCapture();
}
