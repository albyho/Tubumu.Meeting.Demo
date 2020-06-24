namespace TubumuMeeting.Libuv
{
    public interface ITryWrite<TData>
    {
        int TryWrite(TData data);
    }
}
