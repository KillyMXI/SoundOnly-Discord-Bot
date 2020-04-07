using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using System;
using System.Linq;
using System.Threading.Tasks;
using WinMM;

namespace SoundOnlyBot.Commands
{
    internal class WimMmCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WaveIn? _waveIn = null;
        private readonly object _locker = new object();

        public WimMmCommands(BotState state)
            => _state = state;

        [Command("winmm")]
        [RequireOwner]
        [RequireGuild]
        public async Task PlayWaveIn(CommandContext ctx, int? index = null)
        {
            await _state.LeaveAsync();

            var memberVoiceChannel = ctx.Member?.VoiceState?.Channel;
            if (memberVoiceChannel == null)
            {
                await ctx.RespondAsync("You need to be in a voice channel.");
                return;
            }

            var devices = WaveIn.Devices;

            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < devices.Count)
            {
                deviceIndex = index.Value;
            }
            else
            {
                var lines = devices.Select((d, i) => $"`{i}. {d.Name}`");

                var embed = new DiscordEmbedBuilder
                {
                    Description = string.Join("\n", lines.ToArray()),
                    Footer = new DiscordEmbedBuilder.EmbedFooter { Text = "Reply with a number to select one." }
                };

                var endpointListMsg = await ctx.RespondAsync("Available endpoints:", embed: embed);
                var interactivityResult = await ctx.Client.GetInteractivity()
                    .WaitForMessageAsync(msg => msg.Author == ctx.User, TimeSpan.FromSeconds(60));
                if (interactivityResult.TimedOut)
                {
                    await ctx.RespondAsync("No selection provided.");
                    return;
                }
                if (!int.TryParse(interactivityResult.Result.Content, out deviceIndex))
                {
                    await ctx.RespondAsync("Expected a number, got something else instead.");
                    return;
                }
                if (deviceIndex < 0 || deviceIndex >= devices.Count)
                {
                    await ctx.RespondAsync("No device under this number.");
                    return;
                }
            }

            var format = WaveFormat.Pcm48Khz16BitStereo;
            _waveIn = new WaveIn(deviceIndex)
            {
                BufferQueueSize = 960,
                BufferSize = 960
            };

            _state.LeaveFunc = async () =>
            {
                if (_waveIn != null)
                {
                    lock (_locker)
                    {
                        _waveIn.Dispose();
                        _waveIn = null;
                    }
                }
                await Task.Delay(50);
                ctx.Client.GetVoiceNext().GetConnection(ctx.Guild)?.Disconnect();
            };

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();
            transmitStream.Write(new byte[96000], 0, 96000);

            _waveIn.DataReady
                += (sender, e)
                =>
                {
                    lock (_locker)
                    {
                        transmitStream.Write(e.Data, 0, e.Data.Length);
                    }
                };

            _waveIn.Open(format);
            _waveIn.Start();
            transmitStream.Write(new byte[96000], 0, 96000);
        }
    }
}
