using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class SetConsumerPreferedLayersRequest : ConsumerLayers
    {
        public string ConsumerId { get; set; }
    }
}
