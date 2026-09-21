using System.Text.Json.Serialization;

namespace SmartZoom.Core.Routing;

/// <summary>
/// The name of a zoom strategy, as it appears in the settings file: "Browser", "CtrlWheel", "Reader",
/// "WordCom", "ExcelCom", or "None" to switch SmartZoom off for an application.
/// </summary>
/// <remarks>
/// Ids are strings rather than an enum so that adding a strategy is adding a class: an adapter declares its
/// own id in <see cref="AdapterDescriptor"/>, and no shared list has to be edited to keep up. Comparison
/// ignores case, because the settings file is written by hand.
/// </remarks>
[JsonConverter(typeof(AdapterIdJsonConverter))]
public readonly struct AdapterId : IEquatable<AdapterId>
{
    /// <summary>The application is deliberately not handled; a trigger there does nothing.</summary>
    public static AdapterId None { get; } = new("None");

    /// <summary>Creates an id.</summary>
    /// <param name="value">The name, e.g. "Browser". Surrounding whitespace is trimmed.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    public AdapterId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>The name. Empty only for a default-constructed id, which never reaches the router.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(AdapterId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AdapterId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    /// <summary>Compares two ids, ignoring case.</summary>
    public static bool operator ==(AdapterId left, AdapterId right) => left.Equals(right);

    /// <summary>Compares two ids, ignoring case.</summary>
    public static bool operator !=(AdapterId left, AdapterId right) => !left.Equals(right);
}
