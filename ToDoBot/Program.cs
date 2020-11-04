using BaseBotLib.Interfaces.Logger;
using BaseBotLib.Services.Bot;
using BaseBotLib.Services.Logger;
using BaseBotLib.Services.Storage;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using ToDoBot.Interfaces.Storage;
using ToDoBot.Services.Bot;
using ToDoBot.Services.Storage;

namespace ToDoBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var consoleLogger = new ConsoleLogger();

            try
            {
                var configs = GetConfiguration();

                var botId = configs["AppSettings:bot.id"];
                var botToken = configs["AppSettings:bot.token"];

                var logger = new FileConsoleLogger(configs["AppSettings:logs.folder"], consoleLogger);
                var storage = new FileStorage(configs["AppSettings:storage.fileName"], logger);

                var trackerInfoStorage = GetDbTrackerStorage(configs, logger);

                var bot = new Bot(botId, botToken, storage, logger);
                var ownerBot = new OwnerBot(bot, logger, trackerInfoStorage);

                logger.Info("Запускаем бота.");

                var timer = 0.0;
                while (true)
                {
                    try
                    {
                        await ownerBot.CheckRequests();

                        await ownerBot.SendWords();
                    }
                    catch (Exception exc)
                    {
                        logger.Error($"Ошибка при обработке сообщения : {exc}");
                    }

                    if (timer > 60)
                    {
                        logger.Info($"Ping bot. Success.");
                        timer = 0;
                    }

                    timer += 0.2;
                    await Task.Delay(20);
                }

                logger.Info("Работа программы завершена. Нажмите любую клавишу для выхода.");
                Console.ReadKey();
            }
            catch (Exception exc)
            {
                consoleLogger.Error("Не удалось запустить бота. Завершаем работу.");
                consoleLogger.Error($"Exception : {exc}");
            }
        }

        private static IInfoStorage GetDbTrackerStorage(IConfiguration configs, ILogger logger)
        {
            var path = configs["AppSettings:trackerStorage.fileDb"];
            var folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return new ToDoInfoSqlLiteStorage(path, logger);
        }

        private static IConfiguration GetConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            return builder.Build();
        }
    }
}