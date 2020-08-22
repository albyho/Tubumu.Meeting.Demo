using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Tubumu.Core.Extensions
{
    /// <summary>
    /// EnumExtensions
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// 根据枚举值，获取枚举的DisplayName
        /// </summary>
        /// <param name="enumValue">枚举值</param>
        /// <returns>DisplayName</returns>
        public static string GetEnumDisplayName(this object enumValue)
        {
            if (enumValue == null)
            {
                throw new ArgumentNullException(nameof(enumValue));
            }

            var type = enumValue.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(enumValue), "The parameter named \"enumValue\" is not an enum value.");
            }

            return GetEnumDisplayName(enumValue, type);
        }

        /// <summary>
        /// 根据枚举的类型，获取枚举值与DisplayName形成的字典
        /// </summary>
        /// <typeparam name="T">泛型参数</typeparam>
        /// <param name="type">枚举类型</param>
        /// <returns>枚举值与DisplayName形成的字典</returns>
        public static IEnumerable<KeyValuePair<T, string>> GetEnumDictionary<T>(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!type.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(type), "The parameter named \"type\" is not an enum.");
            }

            return from e in Enum.GetValues(type).Cast<T>()
                   select new KeyValuePair<T, string>(e, e.GetEnumDisplayName(type));
        }

        /// <summary>
        /// 根据枚举的类型，获取枚举值与DisplayName形成的字典(非扩展方法)
        /// </summary>
        /// <typeparam name="T">泛型参数</typeparam>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<T, string>> GetEnumDictionary<T>()
        {
            return GetEnumDictionary<T>(typeof(T));
        }

        /// <summary>
        /// 获取枚举原始常量值
        /// </summary>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        public static string GetEnumRawConstantValue(this object enumValue)
        {
            if (enumValue == null)
            {
                throw new ArgumentNullException(nameof(enumValue));
            }

            var type = enumValue.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(enumValue), "The parameter named \"enumValue\" is not an enum value.");
            }

            return GetEnumRawConstantValue(enumValue, type);
        }

        public static string GetEnumStringValue(this Enum enumValue)
        {
            var type = enumValue.GetType();
            var enumName = Enum.GetName(type, enumValue);
            var attributes = type.GetField(enumName).GetCustomAttributes(typeof(EnumStringValueAttribute), false);
            if (attributes.Length > 0)
            {
                return ((EnumStringValueAttribute)attributes[0]).Value;
            }
            else
            {
                return null;
            }
        }

        public static string GetEnumStringValue(this object enumValue)
        {
            var type = enumValue.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(enumValue), "The parameter named \"enumValue\" is not an enum value.");
            }

            var enumName = Enum.GetName(type, enumValue);
            var attributes = type.GetField(enumName).GetCustomAttributes(typeof(EnumStringValueAttribute), false);
            if (attributes.Length > 0)
            {
                return ((EnumStringValueAttribute)attributes[0]).Value;
            }
            else
            {
                return null;
            }
        }

        public static string[] GetEnumStringValues(this Type enumType)
        {
            if (!enumType.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(enumType), "The parameter named \"enumValue\" is not an enum type.");
            }

            var fields = enumType.GetFields();
            var result = new List<string>();
            foreach (var field in fields)
            {
                var attribute = field.GetCustomAttributes(typeof(EnumStringValueAttribute), false).FirstOrDefault();
                if (attribute != null)
                {
                    result.Add(((EnumStringValueAttribute)attribute).Value);
                }
            }

            return result.ToArray();
        }

        public static IDictionary<string, T> GetEnumStringValueMap<T>(this Type enumType)
        {
            if (!enumType.IsEnum)
            {
                throw new ArgumentOutOfRangeException(nameof(enumType), "The parameter named \"enumValue\" is not an enum type.");
            }

            var values = Enum.GetValues(enumType).Cast<T>();
            var result = new Dictionary<string, T>();
            foreach (var value in values)
            {
                var field = enumType.GetField(Enum.GetName(enumType, value));
                var attribute = field.GetCustomAttributes(typeof(EnumStringValueAttribute), false).FirstOrDefault();
                if (attribute != null)
                {
                    result.Add(((EnumStringValueAttribute)attribute).Value, value);
                }
            }

            return result;
        }

        #region Private Methods

        private static string GetEnumRawConstantValue(this object enumValue, Type type)
        {
            var filedInfo = type.GetField(Enum.GetName(type, enumValue));
            return filedInfo.GetRawConstantValue().ToString();
        }

        private static string GetEnumDisplayName(this object enumValue, Type type)
        {
            var enumName = Enum.GetName(type, enumValue);
            if (enumName == null)
            {
                return null;
            }

            var attributes = type.GetField(enumName).GetCustomAttributes(typeof(DisplayAttribute), false);
            if (attributes.Length > 0)
            {
                return ((DisplayAttribute)attributes[0]).GetName(); // TODO: (alby)如果 DisplayAttribute 的 DisplayName 属性为 IsNullOrWhiteSpace ,尝试从资源文件获取
            }
            else
            {
                return enumValue.ToString();
            }
        }

        #endregion Private Methods
    }

    public sealed class EnumStringValueAttribute : Attribute
    {
        public string Value { get; set; }

        public EnumStringValueAttribute(string value)
        {
            Value = value;
        }
    }
}
