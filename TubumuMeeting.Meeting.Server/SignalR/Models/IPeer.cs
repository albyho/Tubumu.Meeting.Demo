using System.Threading.Tasks;

namespace TubumuMeeting.Meeting.Server
{
    public interface IPeer
    {
        Task Notify(MeetingNotification notification);
    }
}
