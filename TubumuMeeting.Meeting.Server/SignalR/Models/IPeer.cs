using System.Threading.Tasks;

namespace TubumuMeeting.Meeting.Server
{
    public interface IPeer
    {
        Task NotifyAsync(MeetingNotification notification);
    }
}
