using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class Commands : BaseCommandModule
    {
        private readonly BotState _state;

        public Commands(BotState state)
            => _state = state;

        [Command("leave")]
        [Aliases("disconnect", "dis", "stop")]
        [RequireOwner]
        [RequireGuild]
        public async Task Leave(CommandContext ctx)
            => await _state.LeaveAsync();
    }
}
