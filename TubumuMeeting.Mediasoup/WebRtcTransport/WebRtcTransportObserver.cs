using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransportObserver : TransportObserver
    {
        public event Action<IceState>? IceStateChange;

        public event Action<TransportTuple>? IceSelectedTupleChange;

        public event Action<DtlsState>? DtlsStateChange;

        public event Action<SctpState>? SctpStateChange;

        public event Action<TraceEventData>? Trace;

        public void EmitIceStateChange(IceState iceState) 
        {
            IceStateChange?.Invoke(iceState);
        }

        public void EmitIceSelectedTupleChange(TransportTuple transportTuple)
        {
            IceSelectedTupleChange?.Invoke(transportTuple);
        }

        public void EmitDtlsStateChange(DtlsState dtlsState)
        {
            DtlsStateChange?.Invoke(dtlsState);
        }

        public void EmitSctpStateChange(SctpState sctpState)
        {
            SctpStateChange?.Invoke(sctpState);
        }

        public void EmitTrace(TraceEventData traceEventData)
        {
            Trace?.Invoke(traceEventData);
        }
    }
}
