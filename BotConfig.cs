using System.IO;
using System.Text.Json;

namespace SoundOnlyBot
{
    internal class BotConfig
    {
        public string DiscordApiToken { get; set; } = "your_token";
        public string CommandPrefix { get; set; } = ";;";


        #region Serialization

        private const string DEFAULT_PATH = "bot_config.json";

        // field is ignored by System.Text.Json serializer
        private string _path = DEFAULT_PATH;

        private static readonly JsonSerializerOptions _serializerOptions
            = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

        public static BotConfig? LoadFromFile(string? path = null)
        {
            BotConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<BotConfig>(
                    File.ReadAllText(path ?? DEFAULT_PATH),
                    _serializerOptions
                    );
            }
            catch { return null; }

            if (path != null) { config._path = path; }
            return config;
        }

        public void Save()
            => File.WriteAllText(
                _path,
                JsonSerializer.Serialize(this, _serializerOptions)
                );

        #endregion
    }
}
