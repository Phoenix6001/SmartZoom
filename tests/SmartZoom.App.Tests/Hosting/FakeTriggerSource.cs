using System.Threading.Channels;

using SmartZoom.Core.Input;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>A trigger source a test writes into by hand, which remembers what it was told to watch.</summary>
internal sealed class FakeTriggerSource : ITriggerSource
{
    private readonly Channel<TriggerEvent> _channel = Channel.CreateUnbounded<TriggerEvent>();

    public ChannelWriter<TriggerEvent> Writer => _channel.Writer;

    public ChannelReader<TriggerEvent> Triggers => _channel.Reader;

    public bool Enabled { get; set; } = true;

    /// <summary>The set most recently passed to <see cref="SetTriggers"/>, or null before the first call.</summary>
    public IReadOnlyList<TriggerDefinition>? CurrentTriggers { get; private set; }

    public void SetTriggers(IEnumerable<TriggerDefinition> triggers) => CurrentTriggers = [.. triggers];

    public void StartCapture()
    {
    }

    public void StopCapture()
    {
    }
}
