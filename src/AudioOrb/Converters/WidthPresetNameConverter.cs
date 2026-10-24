using Microsoft.UI.Xaml.Data;

namespace AudioOrb.Converters;

public sealed class WidthPresetNameConverter : IValueConverter
{
    private static readonly string[] Names = ["图标", "紧凑", "标准", "宽"];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var number = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d,
        };

        var index = Math.Clamp((int)Math.Round(number), 0, Names.Length - 1);
        return Names[index];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
