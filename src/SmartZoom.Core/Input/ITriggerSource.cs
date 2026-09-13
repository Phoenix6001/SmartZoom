using System.Threading.Channels;

namespace SmartZoom.Core.Input;

/// <summary>Produces double-tap triggers. The Windows implementation is a low-level mouse hook.</summary>
public interface ITriggerSource
{
    /// <summary>Triggers in the order they were detected. Never completes while the source is running.</summary>
    ChannelReader<TriggerEvent> Triggers { get; }

    /// <summary>When false, all input passes through untouched and no triggers are raised.</summary>
    bool Enabled { get; set; }

    /// <summary>Begins capturing input. Throws if capture cannot be installed.</summary>
    void StartCapture();

    /// <summary>Stops capturing input and releases any held (swallowed) presses.</summary>
    void StopCapture();
}
