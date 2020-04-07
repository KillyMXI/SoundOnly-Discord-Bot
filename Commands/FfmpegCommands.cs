using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using NAudio.CoreAudioApi;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SoundOnlyBot.Commands
{
    internal class FfmpegCommands : BaseCommandModule
    {
        private readonly BotState _state;

        public FfmpegCommands(BotState state)
            => _state = state;
        
        [Command("ffmpeg")]
        [RequireOwner]
        [RequireGuild]
        public async Task PlayFfmpeg(CommandContext ctx, int? index = null)
        {
            await _state.LeaveAsync();

            var memberVoiceChannel = ctx.Member?.VoiceState?.Channel;
            if (memberVoiceChannel == null)
            {
                await ctx.RespondAsync("You need to be in a voice channel.");
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

            var deviceName = endpoinds[deviceIndex].FriendlyName;

            var ffmpegProcess = new Process{ StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = $"-loglevel warning -f dshow -i audio=\"{deviceName}\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            } };

            ffmpegProcess.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                ctx.Client.DebugLogger.LogMessage(
                    LogLevel.Info,
                    "ffmpeg error output",
                    $"{e.Data}",
                    DateTime.Now
                    );
            };

            var cts = new CancellationTokenSource();

            _state.LeaveFunc = async () =>
            {
                cts.Cancel();
                await Task.Delay(50);
                ffmpegProcess.Kill();
                await Task.Delay(50);
                ctx.Client.GetVoiceNext().GetConnection(ctx.Guild)?.Disconnect();
            };

            ffmpegProcess.Start();
            ffmpegProcess.PriorityClass = ProcessPriorityClass.High;
            ffmpegProcess.BeginErrorReadLine();

            var voiceConnection = await memberVoiceChannel.ConnectAsync();
            var transmitStream = voiceConnection.GetTransmitStream();
            transmitStream.Write(new byte[96000], 0, 96000);

            var ffoutStream = ffmpegProcess.StandardOutput.BaseStream;

            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                const int buffSize = 3840; // 48_000 (samples / second) * 2 channels * 2 bytes * 20 (milliseconds / sample) * 0.001 (seconds / millisecond)
                long ticksPerSample = Stopwatch.Frequency * 20 / 1000; // (ticks / second) * 20 (milliseconds / sample) * 0.001 (seconds / millisecond)
                var timestamp = Stopwatch.GetTimestamp();

                while (!token.IsCancellationRequested)
                {
                    ffoutStream.CopyTo(transmitStream, buffSize);
                    var now = Stopwatch.GetTimestamp();
                    timestamp += ticksPerSample;
                    var ticksToWait = timestamp - now - 1000; // arbitrary number from 0 to ticksPerSample
                    if (ticksToWait > 0)
                    {
                        await Task.Delay(TimeSpan.FromTicks(ticksToWait)).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);
        }
    }
}
