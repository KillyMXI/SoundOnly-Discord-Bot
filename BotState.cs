using System;
using System.Threading.Tasks;

namespace SoundOnlyBot
{
    internal class BotState
    {
        private readonly BotConfig _config;

        public BotState(BotConfig config)
            => _config = config;

        public Func<Task>? LeaveFunc { get; set; }

        public async Task LeaveAsync()
        {
            if (LeaveFunc != null)
            {
                var leave = LeaveFunc;
                LeaveFunc = null;
                await leave.Invoke();
            }
        }
    }
}
