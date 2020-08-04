using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Tubumu.Core.Json
{
    public class TypePrefixEnumConverter : StringEnumConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            bool isNullable = (Nullable.GetUnderlyingType(objectType) != null);
            Type enumType = (Nullable.GetUnderlyingType(objectType) ?? objectType);
            if (!enumType.IsEnum)
            {
                throw new JsonSerializationException(string.Format("type {0} is not a enum type", enumType.FullName));
            }

            var prefix = enumType.Name + "_";

            if (reader.TokenType == JsonToken.Null)
            {
                if (!isNullable)
                {
                    throw new JsonSerializationException();
                }

                return null;
            }

            // Strip the prefix from the enum components (if any).
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
            {
                token = (JValue)string.Join(", ", token.ToString().Split(',').Select(s => s.Trim()).Select(s => s.StartsWith(prefix) ? s.Substring(prefix.Length) : s).ToArray());
            }

            using (var subReader = token.CreateReader())
            {
                while (subReader.TokenType == JsonToken.None)
                {
                    subReader.Read();
                }

                return base.ReadJson(subReader, objectType, existingValue, serializer); // Use base class to convert
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var array = new JArray();
            using (var tempWriter = array.CreateWriter())
            {
                base.WriteJson(tempWriter, value, serializer);
            }

            var token = array.Single();

            if (token.Type == JTokenType.String && value != null)
            {
                var enumType = value.GetType();
                var prefix = enumType.Name + "_";
                token = (JValue)string.Join(", ", token.ToString().Split(',').Select(s => s.Trim()).Select(s => (!char.IsNumber(s[0]) && s[0] != '-') ? prefix + s : s).ToArray());
            }

            token.WriteTo(writer);
        }
    }
}
