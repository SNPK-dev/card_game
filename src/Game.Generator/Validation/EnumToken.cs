using System.Reflection;
using System.Text.Json.Serialization;

namespace Game.Generator.Validation;

internal static class EnumToken
{
  public static string ToToken<TEnum>(TEnum value)
    where TEnum : struct, Enum
  {
    var member = typeof(TEnum).GetMember(value.ToString())[0];
    var attribute = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
    return attribute?.Name ?? value.ToString();
  }
}
