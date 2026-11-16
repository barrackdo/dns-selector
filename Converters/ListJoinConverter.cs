using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace DnsSelector.Converters;

/// <summary>
/// List&lt;string&gt; を区切り文字で結合して表示するコンバーター。
/// </summary>
public sealed class ListJoinConverter : IValueConverter
{
    public string Separator { get; set; } = ", ";
    public string EmptyText { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> list)
        {
            var items = new List<string>(list);
            return items.Count > 0 ? string.Join(Separator, items) : EmptyText;
        }
        return EmptyText;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}