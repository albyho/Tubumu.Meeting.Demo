using System;

namespace TubumuMeeting.Mediasoup
{
    public static class Utils
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// 生成 100000000 - 999999999 的随机数
        /// </summary>
        /// <returns></returns>
        public static long GenerateRandomNumber()
        {
            return _random.Next(100000000, 999999999);
        }
    }
}
