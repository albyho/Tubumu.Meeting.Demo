using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Tubumu.Core.Extensions
{
    public static class JsonExtensions
    {
        public static T GetValue<T>(this JToken jToken, T defaultValue = default)
        {
            if (jToken == null)
            {
                return defaultValue;
            }

            object data = null;
            var sData = jToken.ToString();
            Type type = typeof(T);

            if (typeof(double).IsAssignableFrom(type))
            {
                data = double.Parse(sData);
            }
            else if (typeof(bool).IsAssignableFrom(type))
            {
                data = bool.Parse(sData);
            }
            else if (typeof(string).IsAssignableFrom(type))
            {
                data = sData;
            }

            if (null == data && type.IsValueType)
            {
                throw new ArgumentException($"Cannot parse type \"{type.FullName}\" from value \"{ sData }\"");
            }

            var returnValue = (T)Convert.ChangeType(data, type, CultureInfo.InvariantCulture);

            return returnValue;
        }

        public static T Value<T>(this JToken jToken, T defaultValue)
        {
            if (jToken == null)
            {
                return defaultValue;
            }

            return jToken.Value<T>();
        }

        public static string Value(this JToken jToken, string defaultValue)
        {
            if (jToken == null)
            {
                return defaultValue;
            }

            return jToken.ToString();
        }

        public static T ToObject<T>(this JToken jToken, T defaultValue)
        {
            if (jToken == null)
            {
                return defaultValue;
            }

            return jToken.ToObject<T>();
        }

        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
                   (token.Type == JTokenType.Array && !token.HasValues) ||
                   (token.Type == JTokenType.Object && !token.HasValues) ||
                   (token.Type == JTokenType.String && string.IsNullOrEmpty(token.ToString())) ||
                   (token.Type == JTokenType.Null);
        }
    }
}
