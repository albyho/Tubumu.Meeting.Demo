using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Tubumu.Core.Extensions;

namespace Tubumu.Core.Json
{
    public class EnumStringValueConverter : StringEnumConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            bool isNullable = (Nullable.GetUnderlyingType(objectType) != null);
            Type enumType = (Nullable.GetUnderlyingType(objectType) ?? objectType);
            if (!enumType.IsEnum)
            {
                throw new JsonSerializationException(string.Format("type {0} is not a enum type", enumType.FullName));
            }

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
                var enumStringValues = enumType.GetEnumStringValueMap<Enum>();
                token = (JValue)token.ToString().Split(',').Select(s => s.Trim()).Select(s => enumStringValues.TryGetValue(token.ToString(), out var v) ? v.ToString() : s).Join(",");
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
                var tempToken = token.ToString().Split(',').Select(s => s.Trim()).Select(s => value.GetEnumStringValue() ?? s);
                if (!tempToken.IsNullOrEmpty())
                {
                    token = tempToken.Join(",");
                }
            }

            token.WriteTo(writer);
        }
    }
}
