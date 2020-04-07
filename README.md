# SoundOnly Discord bot

This is a proof-of-concept. And so far it's not even a great proof.

The goal is to stream the sound from local PC sound devices in a simple and straightforward manner. Not intended to scale - one owner, one stream at a time is good enough.

## Dependencies

- [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus) version 4 (manual actions needed to provide VoiceNext requirements);
- [NAudio](https://github.com/naudio/NAudio).

## Configuration

After the first run with no config file, the app will create a dummy config (`bot_config.json` in the same folder) and exit. Fill the config with your own bot token and command prefix.

## Available commands

- `wavein` - uses [WaveInEvent](https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveInputs/WaveInEvent.cs) to get audio from capture devices (microphones and such);
- `wasapi` - uses [WasapiCapture](https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveInputs/WasapiCapture.cs) to get audio from capture devices (microphones and such);
- `loopback` - uses [WasapiLoopbackCapture](https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveInputs/WasapiLoopbackCapture.cs) to get audio from "render" devices (speakers and such);
- `ffmpeg` - uses ffmpeg process to get audio from capture devices (put `ffmpeg.exe` near the bot binary);
- `leave` - stop any active stream and leave voice channel.

All activating commands have an optional argument. Instead if seeing the device list and replying with a number every time, the number can be provided as an argument.

## Notes

[Virtual Audio Cable](https://www.vb-audio.com/Cable/index.htm) and/or [Voicemeeter](https://www.vb-audio.com/Voicemeeter/banana.htm) are extremely handy to route local sounds.

NAudio is a poorly documented mess. [CSCore](https://github.com/filoe/cscore) seems to have more polish, but it haven't made the transition to .NET Core yet, and it's questionable whether it will ever be able to do it. Activity on that project is quite low at the moment. *(The project name now looks quite ironic.)*

## Challenges

- Some sound devices don't want to cooperate. Expect crashes. So far I didn't try to recover from such failures - I want no ensure I can have working devices to work properly in the first place;

- It's not always possible to have `WaveFormat` you need (Discord requires PCM / 48000 Hz / 16 bps / stereo). `WaveInEvent` is the easiest in that sense - it is able to return data in requested format for all devices I tested. `WasapiLoopbackCapture` don't have wave format settings - have to deal with what it returns. `WasapiCapture` was able to use the required format only for one microphone and returned a completely oddball format when requested for the "closest" format for any other devices;

- NAudio is like a set of incompatible bricks. In case of `WasapiLoopbackCapture` and `WasapiCapture` I had to come up with format transformations - I'm not sure they are the most efficient ones. You might also need a different set of transformations depending on your PC configuration. I stopped with what "works" for me;

- The major issue is that this bot does not achieve the transmission quality suitable for music streaming. Sound glitches happen ever so often even with `WaveInEvent`. So far I'm not entirely sure if there is a single root cause that can be removed and make the whole thing meaningful for further improvement;

- ASIO support from NAudio left unimplemented. It requires [single-threaded apartment state](https://docs.microsoft.com/en-us/dotnet/api/system.threading.thread.setapartmentstate). Not worth an effort unless the above issue is sorted out first. Also can't just slap `[STAThread]` on top of `Main()`;

- It might be interesting idea to handle the return channel and allow to use this as a voice gateway. It might suffice for voice comms the way it is already. But it wasn't my goal, so I leave it for "maybe later";

- CSCore can be used despite the lack of .NET Core support. It doesn't solve the sound issues though (`cscore` branch);

- FFmpeg can be used to capture sound in a way similar to playing files. It produces somewhat better result than all other attempts so far, but still not perfect.

## License

MIT
