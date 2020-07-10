using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;

namespace TubumuMeeting.Meeting.Server
{
    public static class SignalRExtensions
    {
        public static HttpContext? GetHttpContext(this HubCallerContext context)
        {
            return context
               ?.Features
               .Select(x => x.Value as IHttpContextFeature)
               .FirstOrDefault(x => x != null)
               ?.HttpContext;
        }

        public static T GetQueryParameterValue<T>(this IQueryCollection httpQuery, string queryParameterName)
        {
#pragma warning disable CS8603 // 可能的 null 引用返回。
            return httpQuery.TryGetValue(queryParameterName, out var value) && value.Any()
               ? (T)Convert.ChangeType(value.FirstOrDefault(), typeof(T))
               : default(T);
#pragma warning restore CS8603 // 可能的 null 引用返回。
        }

        public static Dictionary<string, object> ToDictionary(this IQueryCollection httpQuery)
        {
            var dictionary = new Dictionary<string, object>();
            foreach (var item in httpQuery)
            {
                dictionary.Add(item.Key, item.Value);
            }
            return dictionary;
        }

        public static void FillToDictionary(this IQueryCollection httpQuery, Dictionary<string, object> dictionary)
        {
            foreach (var item in httpQuery)
            {
                dictionary.Add(item.Key, item.Value);
            }
        }
    }
}
