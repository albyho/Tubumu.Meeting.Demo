namespace TubumuMeeting.Libuv
{
    public interface ITrySend<TMessage>
    {
        int TrySend(TMessage message);
    }
}

