using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoundOnlyBot.Commands;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SoundOnlyBot
{
    static class Program
    {
        const string CONFIG_FILE_NAME = "bot_config.json";

        static void Main(string[] args)
            => MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();

        static async Task MainAsync(string[] _args)
        {
            var config = BotConfig.LoadFromFile(CONFIG_FILE_NAME);
            if (config == null)
            {
                // create dummy config and exit
                config = new BotConfig();
                config.Save();
                return;
            }

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            var client = new DiscordClient(new DiscordConfiguration
            {
                Token = config.DiscordApiToken,
                TokenType = TokenType.Bot,
                MinimumLogLevel = LogLevel.Debug
            });

            client.ClientErrored
                += async (DiscordClient sender, ClientErrorEventArgs e)
                => Log(client, LogLevel.Error, $"Client errored in {e.EventName}", e.Exception);
            client.SocketErrored
                += async (DiscordClient sender, SocketErrorEventArgs e)
                => Log(client, LogLevel.Error, $"Socket errored", e.Exception);

            _ = client.UseVoiceNext(new VoiceNextConfiguration { EnableIncoming = false });
            _ = client.UseInteractivity(new InteractivityConfiguration
            {
                Timeout = TimeSpan.FromMinutes(2)
            });

            var commands = client.UseCommandsNext(new CommandsNextConfiguration
            {
                EnableDms = false,
                StringPrefixes = new[] { config.CommandPrefix },
                Services = new ServiceCollection()
                    .AddSingleton(new BotState(config))
                    .BuildServiceProvider()
            });

            commands.RegisterCommands<Commands.Commands>();
            commands.RegisterCommands<LoopbackCommands>();
            commands.RegisterCommands<WasapiCommands>();
            commands.RegisterCommands<WaveInCommands>();
            commands.RegisterCommands<FfmpegCommands>();

            commands.CommandExecuted
                += async (CommandsNextExtension sender, CommandExecutionEventArgs e)
                => Log(client, LogLevel.Debug, $"{e.Context.User.Username} executed '{e.Command.QualifiedName}'");
            commands.CommandErrored
                += async (CommandsNextExtension sender, CommandErrorEventArgs e)
                => Log(client, LogLevel.Error, $"{e.Context.User.Username} tried executing '{e.Context.Message.Content}' but it errored", e.Exception);

            AppDomain.CurrentDomain.UnhandledException
                += (object sender, UnhandledExceptionEventArgs e)
                => Log(client, LogLevel.Error, $"{AppDomain.CurrentDomain.FriendlyName}\nUnhandled exception happened:\n------\n{e.ExceptionObject}\n------");

            await client.ConnectAsync();
            await Task.Delay(-1);
        }

        public static void Log(DiscordClient client, LogLevel level, string message, Exception? exception = null)
        {
            if (exception != null)
            {
                client.Logger.Log(level, exception, message);
            }
            else
            {
                client.Logger.Log(level, message);
            }
        }
    }
}
