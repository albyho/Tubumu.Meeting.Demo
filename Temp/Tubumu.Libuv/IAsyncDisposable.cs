using System.Threading.Tasks;

namespace Tubumu.Libuv
{
    interface IAsyncDisposable
    {
        Task DisposeAsync();
    }
}
