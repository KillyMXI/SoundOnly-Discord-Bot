using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class LoopbackCommands : BaseCommandModule
    {
        private readonly BotState _state;
        private WasapiLoopbackCapture? _wasapiLoopbackCapture;

        private readonly object _locker = new object();
        private BufferedWaveProvider? _bufProvider;
        private WaveFloatTo16Provider? _pcmProvider;
        private byte[] _byteBuffer = Array.Empty<byte>();

        public LoopbackCommands(BotState state)
            => _state = state;

        [Command("loopback")]
        [RequireOwner]
        [RequireGuild]
        public async Task PlayLoopback(CommandContext ctx, int? index = null)
        {
            await _state.LeaveAsync();

            var memberVoiceChannel = ctx.Member?.VoiceState?.Channel;
            if (memberVoiceChannel == null)
            {
                await ctx.RespondAsync("You need to be in a voice channel.");
                return;
            }

            var enumerator = new MMDeviceEnumerator();
            var endpoinds = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

            int deviceIndex = 0;
            if (index.HasValue && 0 <= index && index < endpoinds.Length)
            {
                deviceIndex = index.Value;
            }
            else
            {
                var lines = endpoinds.Select((ep, i) => $"`{i}. {ep.FriendlyName}`");

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
                if (deviceIndex < 0 || deviceIndex >= endpoinds.Length)
                {
                    await ctx.RespondAsync("No device under this number.");
                    return;
                }
            }

            _wasapiLoopbackCapture = new WasapiLoopbackCapture(endpoinds[deviceIndex]);
            var wf = _wasapiLoopbackCapture.WaveFormat;

            if (wf.SampleRate != 48000 || wf.Channels != 2)
            {
                await ctx.RespondAsync($"Expected a stereo sound source with SampleRate 48000, got {wf.SampleRate}, {wf.Channels} channels instead.");
                return;
            }

            if (wf.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                await ctx.RespondAsync($"Expected a sound source with IeeeFloat encodedd values, got {wf.Encoding.ToString()} instead.");
                return;
            }

            _bufProvider = new BufferedWaveProvider(wf) { ReadFully = false };
            _pcmProvider = new WaveFloatTo16Provider(_bufProvider);
            _bufProvider.AddSamples(new byte[20480], 0, 20480);
            var bpsIn = wf.BitsPerSample;
            var bpsOut = _pcmProvider.WaveFormat.BitsPerSample;

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();

            _wasapiLoopbackCapture.DataAvailable
                += (sender, e)
                =>
                {
                    lock (_locker)
                    {
                        _bufProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                        var outBytesNumber = e.BytesRecorded * bpsOut / bpsIn;
                        _byteBuffer = BufferHelpers.Ensure(_byteBuffer, outBytesNumber);
                        var bytesRead = _pcmProvider.Read(_byteBuffer, 0, outBytesNumber);
                        transmitStream.Write(_byteBuffer, 0, bytesRead);
                    }
                };

            _state.LeaveFunc = async () =>
            {
                if (_wasapiLoopbackCapture != null)
                {
                    _wasapiLoopbackCapture.StopRecording();
                    _wasapiLoopbackCapture.Dispose();
                    _wasapiLoopbackCapture = null;
                }

                await Task.Delay(100);

                var voiceConnection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);
                if (voiceConnection != null)
                {
                    voiceConnection.Disconnect();
                }
            };

            _wasapiLoopbackCapture.StartRecording();
            transmitStream.Write(new byte[192000], 0, 192000);
        }
    }
}
