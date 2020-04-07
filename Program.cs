using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.DependencyInjection;
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
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            client.ClientErrored += OnClientErrored;
            client.SocketErrored += OnSocketErrored;

            _ = client.UseVoiceNext();
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

            commands.CommandExecuted += OnCommandExecutedAsync;
            commands.CommandErrored += OnCommandErroredAsync;

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                client.DebugLogger.LogMessage(
                    LogLevel.Error,
                    AppDomain.CurrentDomain.FriendlyName,
                    $"unhandled exception happened:\n------\n{e.ExceptionObject}\n------",
                    DateTime.Now
                    );
            };

            await client.ConnectAsync();
            await Task.Delay(-1);
        }

        private static Task OnSocketErrored(SocketErrorEventArgs e)
        {
            e.Client.DebugLogger.LogMessage(
                LogLevel.Error,
                nameof(OnSocketErrored),
                $"socket errored: {e.Exception}",
                DateTime.Now,
                e.Exception
                );
            return Task.CompletedTask;
        }

        private static Task OnClientErrored(ClientErrorEventArgs e)
        {
            e.Client.DebugLogger.LogMessage(
                LogLevel.Error,
                nameof(OnClientErrored),
                $"client errored in {e.EventName}: {e.Exception}",
                DateTime.Now,
                e.Exception
                );
            return Task.CompletedTask;
        }

        private static Task OnCommandExecutedAsync(CommandExecutionEventArgs e)
        {
            e.Context.Client.DebugLogger.LogMessage(
                LogLevel.Debug,
                nameof(OnCommandExecutedAsync),
                $"{e.Context.User.Username} executed '{e.Command.QualifiedName}'",
                DateTime.Now
                );
            return Task.CompletedTask;
        }

        private static Task OnCommandErroredAsync(CommandErrorEventArgs e)
        {
            e.Context.Client.DebugLogger.LogMessage(
                LogLevel.Error,
                nameof(OnCommandErroredAsync),
                $"{e.Context.User.Username} tried executing '{e.Context.Message.Content}' but it errored:\n{e.Exception}",
                DateTime.Now,
                e.Exception
                );
            return Task.CompletedTask;
        }
    }
}
