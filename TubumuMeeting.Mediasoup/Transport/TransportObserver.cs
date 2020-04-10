using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class TransportObserver
    {
        public event Action? Close;

        public event Action<Producer>? NewProducer;

        public event Action<Consumer>? NewConsumer;

        public event Action<DataProducer>? NewDataProducer;

        public event Action<DataConsumer>? NewDataConsumer;

        public void EmitClose()
        {
            Close?.Invoke();
        }

        public void EmitNewProducer(Producer producer)
        {
            NewProducer?.Invoke(producer);
        }

        public void EmitNewConsumer(Consumer consumer)
        {
            NewConsumer?.Invoke(consumer);
        }

        public void EmitNewDataProducer(DataProducer dataProducer)
        {
            NewDataProducer?.Invoke(dataProducer);
        }

        public void EmitNewDataConsumer(DataConsumer dataConsumer)
        {
            NewDataConsumer?.Invoke(dataConsumer);
        }
    }
}
