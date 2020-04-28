using System;

namespace TubumuMeeting.Mediasoup
{
    public static class Utils
    {
        private static readonly Random _random = new Random();

        public static long GenerateRandomNumber()
        {
            return _random.Next(100000000, 999999999);
        }
    }
}
