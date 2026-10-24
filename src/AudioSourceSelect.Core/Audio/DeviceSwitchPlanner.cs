namespace AudioSourceSelect.Core.Audio;

/// <summary>
/// Pure switch-order logic, kept separate from COM and UI so it is easy to test.
/// </summary>
public static class DeviceSwitchPlanner
{
    /// <summary>
    /// Returns the next device id in the user's ordered selection, or null when
    /// there is nothing to do.
    /// </summary>
    /// <param name="enabledDeviceIds">User-ordered enabled device ids.</param>
    /// <param name="activeDeviceIds">Ids of currently active render endpoints.</param>
    /// <param name="currentDefaultId">The current default render endpoint, if any.</param>
    /// <param name="direction">1 for next, -1 for previous.</param>
    public static string? ChooseNext(
        IEnumerable<string> enabledDeviceIds,
        IEnumerable<string> activeDeviceIds,
        string? currentDefaultId,
        int direction = 1)
    {
        ArgumentNullException.ThrowIfNull(enabledDeviceIds);
        ArgumentNullException.ThrowIfNull(activeDeviceIds);

        var active = activeDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var order = enabledDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && active.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (order.Count == 0)
        {
            return null;
        }

        var currentIndex = string.IsNullOrWhiteSpace(currentDefaultId)
            ? -1
            : order.FindIndex(id => string.Equals(id, currentDefaultId, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
        {
            return direction < 0 ? order[^1] : order[0];
        }

        if (order.Count == 1)
        {
            return null;
        }

        var step = direction < 0 ? -1 : 1;
        var nextIndex = (currentIndex + step + order.Count) % order.Count;
        return order[nextIndex];
    }

    /// <summary>
    /// Returns enabled endpoints in selection order, filtered to active devices.
    /// </summary>
    public static IReadOnlyList<AudioEndpoint> ResolveEnabledEndpoints(
        IEnumerable<string> enabledDeviceIds,
        IEnumerable<AudioEndpoint> endpoints)
    {
        var byId = endpoints.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AudioEndpoint>();

        foreach (var id in enabledDeviceIds)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !seen.Add(id) ||
                !byId.TryGetValue(id, out var endpoint))
            {
                continue;
            }

            result.Add(endpoint);
        }

        return result;
    }
}
