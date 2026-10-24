using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using AudioSourceSelect.Core.Audio;

namespace AudioOrb.Models;

public sealed class DeviceRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isDefault;
    private int _order;
    private double _rowWidth;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Detail { get; init; }

    public required string IconGlyph { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrderLabel));
            OnPropertyChanged(nameof(OrderVisibility));
        }
    }

    public bool IsDefault
    {
        get => _isDefault;
        private set
        {
            if (_isDefault == value)
            {
                return;
            }

            _isDefault = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultVisibility));
        }
    }

    public int Order
    {
        get => _order;
        private set
        {
            if (_order == value)
            {
                return;
            }

            _order = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrderLabel));
            OnPropertyChanged(nameof(OrderVisibility));
        }
    }

    public double RowWidth
    {
        get => _rowWidth;
        set
        {
            if (Math.Abs(_rowWidth - value) < 0.5)
            {
                return;
            }

            _rowWidth = value;
            OnPropertyChanged();
        }
    }

    public Visibility DefaultVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OrderVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public string OrderLabel => IsSelected && Order > 0 ? $"切换顺序 {Order}" : "未参与切换";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static DeviceRow FromEndpoint(AudioEndpoint endpoint, bool selected, int order)
    {
        var kind = endpoint.IsHeadphones ? "耳机" : endpoint.FormFactorLabel;
        var detail = string.IsNullOrWhiteSpace(endpoint.AdapterName)
            ? kind
            : $"{kind} · {endpoint.AdapterName}";

        return new DeviceRow
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            Detail = detail,
            IconGlyph = endpoint.IconGlyph,
            _isSelected = selected,
            _isDefault = endpoint.IsDefault,
            _order = order,
        };
    }

    public void Update(AudioEndpoint endpoint, bool selected, int order)
    {
        IsSelected = selected;
        IsDefault = endpoint.IsDefault;
        Order = order;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
