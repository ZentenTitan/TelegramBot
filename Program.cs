using FinancialSystem.Services;
using System.Threading.Tasks;

namespace FinancialSystem
{
    class Program
    {
        static async Task Main()
        {
            new TelegramService().InitializeAsync();
            await Task.Delay(-1);
        }
    }
}
