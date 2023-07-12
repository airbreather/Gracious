using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.VoiceNext;

using Gracious;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

hostBuilder.ConfigureAppConfiguration(builder =>
{
    builder.AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "secret-discord-config.json"), optional: false, reloadOnChange: true);
});

hostBuilder.ConfigureServices(
    services =>
    {
        services.AddLogging(builder =>
        {
            builder.AddSerilog();
        });

        services.AddOptions<GraciousConfiguration>()
            .BindConfiguration("Gracious");

        services.AddOptions<DiscordConfiguration>()
            .Configure<IOptions<GraciousConfiguration>, ILoggerFactory>((discordCfg, graciousCfg, loggerFactory) =>
            {
                discordCfg.TokenType = TokenType.Bot;
                discordCfg.AutoReconnect = true;

                discordCfg.Token = graciousCfg.Value.DiscordBotToken;

                discordCfg.LoggerFactory = loggerFactory;
            });

        services.AddOptions<VoiceNextConfiguration>()
            .Configure(voiceCfg =>
            {
                voiceCfg.EnableIncoming = true;
            });

        services.AddSingleton<DiscordClient>(serviceProvider =>
        {
            DiscordConfiguration discordCfg = serviceProvider.GetRequiredService<IOptions<DiscordConfiguration>>().Value;
            VoiceNextConfiguration voiceCfg = serviceProvider.GetRequiredService<IOptions<VoiceNextConfiguration>>().Value;
            GraciousConfiguration graciousCfg = serviceProvider.GetRequiredService<IOptions<GraciousConfiguration>>().Value;

            SlashCommandsConfiguration cmdCfg = new()
            {
                Services = serviceProvider,
            };

            DiscordClient discord = new(discordCfg);
            try
            {
                discord.UseVoiceNext(voiceCfg);

                var slashCommands = discord.UseSlashCommands(cmdCfg);
                foreach (ulong guildId in graciousCfg.GuildIdsForApplicationCommands)
                {
                    slashCommands.RegisterCommands<GraciousCommandModule>(guildId);
                }

                if (graciousCfg.RegisterDefaultApplicationCommands)
                {
                    slashCommands.RegisterCommands<GraciousCommandModule>();
                }

                return discord;
            }
            catch
            {
                discord.Dispose();
                throw;
            }
        });

        services.AddSingleton<GraciousSessions>();
        services.AddHostedService<GraciousBot>();
    });

await hostBuilder.RunConsoleAsync();
