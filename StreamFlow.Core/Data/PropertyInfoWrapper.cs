using System.Reflection;
namespace StreamFlow.Core.Data;
public class PropertyInfoWrapper(PropertyInfo propertyInfo, object? value, string description)
{
    public string Name => PropertyInfo?.Name ?? "None";
    public PropertyInfo? PropertyInfo { get; set; } = propertyInfo;
    public string? PropertyType => PropertyInfo?.PropertyType.Name;
    public object? Value { get; set; } = value;
    public string? Description { get; set; } = description;
    public bool ExpansionState { get; set; }
}
