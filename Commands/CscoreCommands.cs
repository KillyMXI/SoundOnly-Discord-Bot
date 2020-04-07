using CSCore;
using CSCore.CoreAudioAPI;
using CSCore.SoundIn;
using CSCore.Streams;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class CscoreCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WasapiCapture? _wasapiCapture = null;

        public CscoreCommands(BotState state)
            => _state = state;

        private string DataFlowEmoji(DataFlow df, BaseDiscordClient client)
            => df switch
            {
                DataFlow.Render => DiscordEmoji.FromName(client, ":speaker:"),
                DataFlow.Capture => DiscordEmoji.FromName(client, ":microphone2:"),
                _ => ""
            };

        [Command("cscore")]
        [RequireOwner]
        [RequireGuild]
        public async Task PlayCscoreWasapi(CommandContext ctx, int? index = null)
        {
            await _state.LeaveAsync();

            var memberVoiceChannel = ctx.Member?.VoiceState?.Channel;
            if (memberVoiceChannel == null)
            {
                await ctx.RespondAsync("You need to be in a voice channel.");
                return;
            }

            var devices = MMDeviceEnumerator.EnumerateDevices(DataFlow.All, DeviceState.Active);
            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < devices.Count)
            {
                deviceIndex = index.Value;
            }
            else
            {
                var lines = devices.Select((d, i) => $"{DataFlowEmoji(d.DataFlow, ctx.Client)} `{i}. {d.FriendlyName}`");

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

            var device = devices[deviceIndex];
            var waveFormat = device.DeviceFormat;
            const int Latency = 100;

            _wasapiCapture = device.DataFlow == DataFlow.Capture
                ? new WasapiCapture(
                    eventSync:              false,
                    shareMode:              AudioClientShareMode.Shared,
                    latency:                Latency,
                    defaultFormat:          waveFormat,
                    captureThreadPriority:  ThreadPriority.Highest,
                    synchronizationContext: SynchronizationContext.Current
                    ) { Device = device }
                : new WasapiLoopbackCapture(
                    latency:                Latency,
                    defaultFormat:          waveFormat,
                    captureThreadPriority:  ThreadPriority.Highest
                    ) { Device = device };

            _wasapiCapture.Initialize();

            ctx.Client.DebugLogger.LogMessage(
                LogLevel.Info,
                nameof(PlayCscoreWasapi),
                $"\nWaveFormat: {waveFormat}",
                DateTime.Now
                );

            IWaveSource waveSource = new SoundInSource(_wasapiCapture) { FillWithZeros = false };
            if (waveFormat.SampleRate != 48000)
            {
                waveSource = waveSource.ChangeSampleRate(48000);
            }
            if (waveFormat.BitsPerSample != 16)
            {
                waveSource = waveSource.ToSampleSource().ToWaveSource(16);
            }
            if (waveFormat.Channels != 2)
            {
                waveSource = waveSource.ToStereo();
            }

            _state.LeaveFunc = async () =>
            {
                if (_wasapiCapture != null)
                {
                    _wasapiCapture.Stop();
                    _wasapiCapture.Dispose();
                    _wasapiCapture = null;
                }
                await Task.Delay(50);
                ctx.Client.GetVoiceNext().GetConnection(ctx.Guild)?.Disconnect();
            };

            _wasapiCapture.Stopped
                += (sender, e)
                => _ = _state.LeaveAsync();

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();
            transmitStream.Write(new byte[96000], 0, 96000);

            _wasapiCapture.DataAvailable
                += (sender, e)
                => waveSource.WriteToStream(transmitStream);

            _wasapiCapture.Start();
        }
    }
}
