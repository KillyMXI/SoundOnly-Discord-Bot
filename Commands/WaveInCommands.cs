using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class WaveInCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WaveInEvent? _waveInEvent = null;

        public WaveInCommands(BotState state)
            => _state = state;

        [Command("wavein")]
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

            var deviceCount = WaveInEvent.DeviceCount;
            var capabilities = new List<WaveInCapabilities>();
            for (int i = 0; i < deviceCount; i++)
            {
                capabilities.Add(WaveInEvent.GetCapabilities(i));
            }

            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < capabilities.Count)
            {
                deviceIndex = index.Value;
            }
            else
            {
                var lines = capabilities.Select((c, i) => $"`{i}. {c.ProductName}`");

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
                if (deviceIndex < 0 || deviceIndex >= capabilities.Count)
                {
                    await ctx.RespondAsync("No device under this number.");
                    return;
                }
            }

            _waveInEvent = new WaveInEvent()
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(48000, 16, 2),
                BufferMilliseconds = 250,
                NumberOfBuffers = 4
            };

            _state.LeaveFunc = async () =>
            {
                if (_waveInEvent != null)
                {
                    _waveInEvent.StopRecording();
                    _waveInEvent.Dispose();
                    _waveInEvent = null;
                }
                await Task.Delay(50);
                ctx.Client.GetVoiceNext().GetConnection(ctx.Guild)?.Disconnect();
            };

            _waveInEvent.RecordingStopped
                += (sender, e)
                => _ = _state.LeaveAsync();

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();
            transmitStream.Write(new byte[96000], 0, 96000);

            _waveInEvent.DataAvailable
                += (sender, e)
                => transmitStream.Write(e.Buffer, 0, e.BytesRecorded);

            _waveInEvent.StartRecording();
            transmitStream.Write(new byte[96000], 0, 96000);
        }
    }
}
