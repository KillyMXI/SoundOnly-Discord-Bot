using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class WasapiCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WasapiCapture? _wasapicapture = null;

        private readonly object _locker = new object();
        private BufferedWaveProvider? _bufProvider;
        private MediaFoundationResampler? _resampler;
        private byte[] _byteBuffer = Array.Empty<byte>();

        public WasapiCommands(BotState state)
            => _state = state;

        [Command("wasapi")]
        [RequireOwner]
        [RequireGuild]
        public async Task PlayWasapi(CommandContext ctx, int? index = null)
        {
            await _state.LeaveAsync();

            var memberVoiceChannel = ctx.Member?.VoiceState?.Channel;
            if (memberVoiceChannel == null)
            {
                _ = await ctx.RespondAsync("You need to be in a voice channel.");
                return;
            }

            var enumerator = new MMDeviceEnumerator();
            var endpoinds = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();

            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < endpoinds.Length)
            {
                deviceIndex = index.Value;
            }
            else
            {
                var lines = endpoinds.Select((ep, i) => $"`{i}. {ep.FriendlyName}` ({ep.DataFlow.ToString()})");

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
                    _ = await ctx.RespondAsync("No selection provided.");
                    return;
                }
                if (!int.TryParse(interactivityResult.Result.Content, out deviceIndex))
                {
                    _ = await ctx.RespondAsync("Expected a number, got something else instead.");
                    return;
                }
                if (deviceIndex < 0 || deviceIndex >= endpoinds.Length)
                {
                    _ = await ctx.RespondAsync("No device under this number.");
                    return;
                }
            }

            var captureDevice = endpoinds[deviceIndex];
            _wasapicapture = new WasapiCapture(captureDevice, true, 20);

            async Task leaveFunc()
            {
                if (_wasapicapture != null)
                {
                    _wasapicapture.StopRecording();
                    _wasapicapture.Dispose();
                    _wasapicapture = null;
                }

                await Task.Delay(100);

                var voiceConnection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);
                if (voiceConnection != null)
                {
                    voiceConnection.Disconnect();
                }
            }

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitSink = voiceConnection.GetTransmitSink();

            var pcm = new WaveFormat(48000, 16, 2);
            var canDoPcm = captureDevice.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, pcm, out var closestFormat);
            if (canDoPcm)
            {
                _wasapicapture.WaveFormat = pcm;
                _wasapicapture.DataAvailable
                    += (sender, e)
                    => transmitSink.WriteAsync(e.Buffer, 0, e.BytesRecorded);

                _state.LeaveFunc = leaveFunc;
                _wasapicapture.StartRecording();
                _ = transmitSink.WriteAsync(new byte[192000], 0, 192000);
            }
            else
            {
                _bufProvider = new BufferedWaveProvider(closestFormat) { ReadFully = true };
                _resampler = new MediaFoundationResampler(_bufProvider, pcm);
                _bufProvider.AddSamples(new byte[20480], 0, 20480);

                var bpsIn = closestFormat.BitsPerSample;
                var spsIn = closestFormat.SampleRate;
                var bpsOut = pcm.BitsPerSample;
                var spsOut = pcm.SampleRate;

                _wasapicapture.WaveFormat = closestFormat;
                _wasapicapture.DataAvailable
                    += (sender, e)
                    =>
                    {
                        lock (_locker)
                        {
                            _bufProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                            var outBytesNumber = e.BytesRecorded * bpsOut / bpsIn * spsOut / spsIn; // ?
                            _byteBuffer = BufferHelpers.Ensure(_byteBuffer, outBytesNumber);
                            var bytesRead = _resampler.Read(_byteBuffer, 0, outBytesNumber);
                            _ = transmitSink.WriteAsync(_byteBuffer, 0, bytesRead);
                        }
                    };

                _state.LeaveFunc = leaveFunc;
                _wasapicapture.StartRecording();
                _ = transmitSink.WriteAsync(new byte[192000], 0, 192000);
            }
        }
    }
}
