using System;

namespace TubumuMeeting.Libuv
{
    internal unsafe class WorkRequest : PermaRequest
    {
        public static readonly int Size = UV.Sizeof(TubumuMeeting.Libuv.RequestType.UV_WORK);

        public WorkRequest()
            : base(Size)
        {
        }

        private Action before;
        private Action after;

        public WorkRequest(Action before, Action after)
            : this()
        {
            this.before = before;
            this.after = after;
        }

        public static void BeforeCallback(IntPtr req)
        {
            var workreq = PermaRequest.GetObject<WorkRequest>(req);
            workreq.before();
        }

        public static void AfterCallback(IntPtr req)
        {
            var workreq = PermaRequest.GetObject<WorkRequest>(req);
            workreq.after();
            workreq.Dispose();
        }
    }
}
