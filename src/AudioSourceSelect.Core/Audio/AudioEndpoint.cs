namespace AudioSourceSelect.Core.Audio;

/// <summary>
/// A render (output) endpoint as reported by the Windows Core Audio APIs.
/// </summary>
public sealed record AudioEndpoint(
    string Id,
    string Name,
    string AdapterName,
    EndpointFormFactor FormFactor,
    bool IsDefault)
{
    /// <summary>PnP container shared by the game/chat endpoints of one headset.</summary>
    public Guid? ContainerId { get; init; }

    public bool IsHeadsetContainer { get; init; }

    public bool IsHeadphoneContainer { get; init; }

    private static readonly string[] HeadphoneKeywords =
    [
        "headphone", "headset", "耳麦", "耳机", "airpods", "buds",
        "inzone", "arctis", "hyperx", "steelseries", "turtle beach",
        "jabra", "bose", "sennheiser", "audio-technica", "ath-",
        "blackshark", "kraken", "virtuoso", "g733", "g pro x",
        "wh-1000", "mdr-", "cloud ii", "mmx",
    ];

    /// <summary>
    /// Some virtual headset endpoints report themselves as Speakers (for
    /// example the game channel of a wireless headset), so fall back to the
    /// endpoint and adapter names when the form factor is generic.
    /// </summary>
    public bool IsHeadphones =>
        FormFactor is EndpointFormFactor.Headphones or EndpointFormFactor.Headset ||
        ContainsKeyword(Name, HeadphoneKeywords) ||
        ContainsKeyword(AdapterName, HeadphoneKeywords);

    /// <summary>
    /// Headsets use the headset-with-microphone glyph in the Windows volume
    /// flyout, while plain headphones use the headphone glyph.
    /// </summary>
    public bool IsHeadset =>
        FormFactor == EndpointFormFactor.Headset || IsHeadsetContainer;

    public bool IsHeadphoneEndpoint =>
        FormFactor == EndpointFormFactor.Headphones || IsHeadphoneContainer;

    public bool IsGameEndpoint =>
        ContainsKeyword(Name, ["game", "游戏"]) ||
        ContainsKeyword(AdapterName, ["game", "游戏"]);

    public bool IsChatEndpoint =>
        ContainsKeyword(Name, ["chat", "communications", "语音"]) ||
        ContainsKeyword(AdapterName, ["chat", "communications", "语音"]);

    /// <summary>
    /// Segoe Fluent Icons glyph matching the flat line icons used by the
    /// Windows 11 volume flyout.
    /// </summary>
    public string IconGlyph
    {
        get
        {
            if (IsHeadset)
            {
                return "\uE95B";
            }

            if (IsHeadphoneEndpoint)
            {
                return "\uE7F6";
            }

            return FormFactor switch
            {
                EndpointFormFactor.DigitalAudioDisplayDevice => "\uE7F4",
                EndpointFormFactor.SPDIF => "\uE767",
                EndpointFormFactor.LineLevel => "\uE767",
                EndpointFormFactor.RemoteNetworkDevice => "\uE968",
                _ => "\uE767",
            };
        }
    }

    public string FormFactorLabel => FormFactor switch
    {
        EndpointFormFactor.Headphones => "耳机",
        EndpointFormFactor.Headset => "耳麦",
        EndpointFormFactor.Speakers => "扬声器",
        EndpointFormFactor.DigitalAudioDisplayDevice => "显示器音频",
        EndpointFormFactor.SPDIF => "S/PDIF",
        EndpointFormFactor.LineLevel => "线路输出",
        EndpointFormFactor.RemoteNetworkDevice => "网络音频",
        EndpointFormFactor.UnknownDigitalPassthrough => "数字音频",
        _ => "音频设备",
    };

    public string ShortId
    {
        get
        {
            var lastBrace = Id.LastIndexOf('}');
            return lastBrace >= 0 && lastBrace + 1 < Id.Length
                ? Id[(lastBrace + 1)..]
                : Id;
        }
    }

    private static bool ContainsKeyword(string? text, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return keywords.Any(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

public readonly record struct AudioSwitchResult(bool Success, string? ErrorMessage)
{
    public static AudioSwitchResult Ok() => new(true, null);

    public static AudioSwitchResult Fail(string message) => new(false, message);
}
