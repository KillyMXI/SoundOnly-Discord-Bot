using CSCore;
using CSCore.SoundIn;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class CscoreWaveInCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WaveIn? _waveIn = null;

        public CscoreWaveInCommands(BotState state)
            => _state = state;

        [Command("cscorewi")]
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

            var devices = WaveInDevice.EnumerateDevices().ToArray();
            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < devices.Length)
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
                if (deviceIndex < 0 || deviceIndex >= devices.Length)
                {
                    await ctx.RespondAsync("No device under this number.");
                    return;
                }
            }

            var device = devices[deviceIndex];
            var pcm = new WaveFormat(48000, 16, 2);
            _waveIn = new WaveIn(pcm)
            {
                Device = device,
                Latency = 100
            };

            _waveIn.Initialize();

            _state.LeaveFunc = async () =>
            {
                if (_waveIn != null)
                {
                    _waveIn.Stop();
                    _waveIn.Dispose();
                    _waveIn = null;
                }
                await Task.Delay(50);
                ctx.Client.GetVoiceNext().GetConnection(ctx.Guild)?.Disconnect();
            };

            _waveIn.Stopped
                += (sender, e)
                => _ = _state.LeaveAsync();

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();
            transmitStream.Write(new byte[96000], 0, 96000);

            _waveIn.DataAvailable
                += (sender, e)
                => transmitStream.Write(e.Data, 0, e.ByteCount);

            _waveIn.Start();
        }
    }
}
