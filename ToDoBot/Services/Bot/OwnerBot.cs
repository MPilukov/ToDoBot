using ToDoBot.Interfaces.Storage;
using System;
using System.Threading.Tasks;
using BaseBotLib.Interfaces.Bot;
using BaseBotLib.Interfaces.Logger;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using ToDoBot.DTO.Storage;

namespace ToDoBot.Services.Bot
{
    public class OwnerBot
    {
        private readonly IBot _bot;
        private readonly ILogger _logger;
        private readonly IInfoStorage _infoStorage;

        public OwnerBot(IBot bot, ILogger logger, IInfoStorage infoStorage)
        {
            _bot = bot;
            _logger = logger;
            _infoStorage = infoStorage;
        }

        public async Task CheckRequests()
        {
            var newMessages = await _bot.GetNewMessages();

            foreach (var newMessage in newMessages)
            {
                _logger.Info($"Новое сообщение для бота : \"{newMessage.Text}\" от пользователя \"{newMessage.UserName}\".");
                await ProcessMessage(newMessage);
            }
        }

        public async Task SendWords()
        {
            var users = await _infoStorage.GetUserIds();
            var dateTimeUtc = DateTime.UtcNow;

            foreach (var userId in users)
            {
                var userData = await GetUserData(userId, null);

                if (!ValidSettings(userData))
                {
                    continue;
                }

                if (InNotDisturbPeriod(userData, dateTimeUtc))
                {
                    await ResetActionsWithUser(userData);

                    continue;
                }

                var totalMinutesD = userData.LastMessageTo == null ? 
                    (double?)null : (dateTimeUtc - userData.LastMessageTo.Value).TotalMinutes;

                var totalMinutes = totalMinutesD == null ? (int?)null : (int)totalMinutesD;

                if (userData.LastMessageTo != null)
                {
                    if ((dateTimeUtc - userData.LastMessageTo.Value).TotalMinutes < userData.Period)
                    {
                        continue;
                    }
                }

                if (userData.Actions.Count > 0)
                {
                    var action = userData.Actions.Pop();
                    if (action == EAction.AddRecord)
                    {
                        await DeleteAddStep(userData.UserId);
                        await Add("Не указано", userData);
                        return;
                    }
                    else
                    {
                        return;
                    }
                }

                if (totalMinutes!= null && userData.LastMessageFrom != null)
                {
                    var totalMinutesM = (int)(dateTimeUtc - userData.LastMessageFrom.Value).TotalMinutes;
                    if (totalMinutesM > totalMinutes)
                    {
                        totalMinutes = totalMinutesM;
                    }
                }

                var minutesCount = totalMinutes ?? userData.Period ?? 8880;

                if (userData.FirstMessageToday)
                {
                    await _bot.SendMessage(userData.ChatId.ToString(), 
                        $"Спрошу вас через {GetTextForMinutes(minutesCount, false)} о том - что вы успели сделать");


                    var userDataToUpdate1 = await GetUserData(userData.UserId);
                    userDataToUpdate1.LastMessageTo = dateTimeUtc;
                    userDataToUpdate1.LastMessageFrom = dateTimeUtc;
                    userDataToUpdate1.FirstMessageToday = false;
                    await SaveUserData(userDataToUpdate1);

                    return;
                }

                var userDataToUpdate2 = await GetUserData(userData.UserId);
                userDataToUpdate2.Actions.Push(EAction.AddRecord);

                var question = $"Чем вы занимались {GetTextForMinutes(minutesCount, true)} ?";

                var answersToday = await GetAnswers(userId);
                if (answersToday.Any())
                {
                    await _bot.CreateKeyboard(userData.ChatId.ToString(),
                        $"{question} Выберите из допустимых вариантов или напишите свой", answersToday.ToArray(), false, true);
                }
                else
                {
                    await _bot.SendMessage(userData.ChatId.ToString(), question);
                }

                userDataToUpdate2.LastMessageTo = dateTimeUtc;
                if (userDataToUpdate2.PreData.ContainsKey(EAction.AddRecord))
                {
                    userDataToUpdate2.PreData[EAction.AddRecord] = minutesCount;
                }
                else
                {
                    userDataToUpdate2.PreData.TryAdd(EAction.AddRecord, minutesCount);
                }

                await SaveUserData(userDataToUpdate2);
            }
        }

        private async Task<List<string>> GetAnswers(int userId)
        {
            var answersToday = new List<string>();

            var allAnswersToday = await _infoStorage.GetNewRecords(userId);
            foreach (var item in allAnswersToday)
            {
                if (string.IsNullOrEmpty(item?.Data))
                {
                    continue;
                }

                if (!answersToday.Any(x => x.Equals(item.Data, StringComparison.InvariantCultureIgnoreCase)))
                {
                    answersToday.Add(item.Data.ToLower());
                }
            }

            return answersToday;
        }

        private async Task DeleteAddStep(int userId)
        {
            var userDataToUpdate = await GetUserData(userId);
            if (userDataToUpdate.Actions.Count > 0)
            {
                var action = userDataToUpdate.Actions.Pop();
                if (action == EAction.AddRecord)
                {
                    // так и должно быть, поп-аем запись
                }
                else
                {
                    userDataToUpdate.Actions.Push(action);
                }
            }

            await SaveUserData(userDataToUpdate);
        }

        private async Task ResetActionsWithUser(UserData userData)
        {
            if (!userData.FirstMessageToday)
            {
                var todayData = await _infoStorage.GetNewRecords(userData.UserId);
                await _infoStorage.ArchiveRecords(userData.UserId, DateTime.UtcNow);

                await ShowResult(userData, todayData);

                var userDataToUpdate = await GetUserData(userData.UserId);
                userDataToUpdate.LastMessageTo = null;
                userDataToUpdate.LastMessageFrom = null;
                userDataToUpdate.FirstMessageToday = true;
                await SaveUserData(userDataToUpdate);
            }
        }

        private async Task ShowResult(UserData userData, List<RecordData> data)
        {
            var dict = new Dictionary<string, double>();

            var sum = 0.0;

            foreach (var item in data.OrderBy(x => x.Date))
            {
                var name = item.Data.ToLower();
                var count = item.Duration;

                sum += count;

                if (dict.TryGetValue(name, out var countExist))
                {
                    dict[name] = count + countExist;
                }
                else
                {
                    dict.Add(name, count);
                }
            }

            var responseList = new List<string>
            {
                "Посмотрим на ваши результаты сегодня : ",
            };
            if (!dict.Any())
            {
                responseList.Add("Ничего не делалось.");
            }

            var idx = 1;
            foreach (var (key, value) in dict.OrderByDescending(x => x.Value))
            {
                var percent = value / sum * 100;
                // округление

                var dataItem = $"{idx}. {key} ({value} : {percent:#.##} %)";
                responseList.Add(dataItem);

                idx++;
            }

            var response = string.Join(Environment.NewLine, responseList);
            await _bot.SendMessage(userData.ChatId.ToString(), response);


            await GetMainMenuMessage(userData.ChatId, userData);
        }

        private static string GetTextForMinutes(int count, bool isLast)
        {
            var str = count.ToString();
            if (str.EndsWith("1") && (count == 1 || count > 20))
            {
                return (isLast ? "последнюю " : "") + $"{count} минуту"; 
            }
            else if ((str.EndsWith("2") || str.EndsWith("3") || str.EndsWith("4")) && (count < 10 || count > 20))
            {
                return (isLast ? "последние " : "") + $"{count} минуты";
            }
            else
            {
                return (isLast ? "последние " : "") + $"{count} минут";
            }
        }
        private async Task<UserData> GetUserData(int userId, int? chatId = null)
        {
            var data = await _infoStorage.GetUser(userId);
            if (data != null)
            {
                return data;
            }

            if (chatId == null)
            {
                return null;
            }

            var userDataToUpdate = new UserData(userId, chatId.Value);
            await SaveUserData(userDataToUpdate);
            return userDataToUpdate;
        }
        private Task SaveUserData(UserData userData)
        {
            return _infoStorage.UpdateUser(userData);
        }
        private async Task ProcessMessage(Message message)
        {
            var msg = message.Text?.ToLower();
            var userData = await GetUserData(message.UserId, message.ChatId);

            switch (msg)
            {
                case "меню":
                    await GetMainMenuMessage(message.ChatId, userData);
                    return;
                case "время м/у итерациями":
                    await CheckWordsPeriodMenu(message, userData);
                    return;
                case "вчера":
                    var date = DateTime.UtcNow.AddDays(-1);
                    await GetInformation(message, userData, date);
                    return;
                case "сегодня":
                    await GetInformation(message, userData, null);
                    return;
                case "часовой пояс":
                    await TimeZoonMenu(message, userData);
                    return;
                case "не беспокоить":
                    await NotDisturbMenu(message, userData);
                    return;
                case "обновить":
                    await SelectRecordMenu(message, userData);
                    return;
                case "настройки":
                    await Settings(message, userData);
                    return;
                case "что было":
                    await GetInformationMenu(message, userData);
                    return;
            }

            if (userData.Actions.Count == 0)
            {
                await GetMainMenuMessage(message.ChatId, userData);
                return;
            }

            var userDataToUpdate = await GetUserData(userData.UserId);
            var preAction = userDataToUpdate.Actions.Pop();
            await SaveUserData(userDataToUpdate);            

            switch (preAction)
            {
                case EAction.AddRecord:
                    await Add(message.Text, userData);
                    return;
                case EAction.UpdateRecord:
                    await Update(message.Text, userData);
                    return;
                case EAction.SetNotDisturb:
                    await SetNotDisturb(message, userData);
                    return;
                case EAction.SetCheckWordsPeriod:
                    await SetCheckWordsPeriod(message, userData);
                    return;
                case EAction.SetTimeZone:
                    await SetTimeZone(message, userData);
                    return;
                case EAction.SelectRecord:
                    await SelectRecord(message, userData);
                    return;
            }
        }

        private static T GetOrDefaultFromSavedData<T>(
            IReadOnlyDictionary<EAction, object> savedData,
            EAction action) where T : struct
        {
            var obj = savedData.TryGetValue(action, out var x) ? x : null;

            var response = default(T);
            if (obj == null)
            {
                return response;
            }

            if (response is T data)
            {
                response = data;
            }

            return response;
        }

        private async Task Add(string text, UserData userData)
        {
            var dateTimeUtc = DateTime.UtcNow;

            var duration = GetOrDefaultFromSavedData<int>(userData.PreData, EAction.AddRecord);

            await _infoStorage.AddRecord(new RecordData
            {
                Date = dateTimeUtc,
                Data = text,
                Id = Guid.NewGuid(),
                UserId = userData.UserId,
                Duration = duration,
            });

            await _bot.SendMessage(userData.ChatId.ToString(), $"Запись добавлена.");

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.LastMessageFrom = dateTimeUtc;
            await SaveUserData(userDataToUpdate);

            await GetMainMenuMessage(userData.ChatId, userData);
        }


        private async Task Update(string text, UserData userData)
        {
            var recordId = GetOrDefaultFromSavedData<Guid>(userData.PreData, EAction.AddRecord);

            if (recordId == default(Guid))
            {
                await _bot.SendMessage(userData.ChatId.ToString(), $"Не найдена запись.");
                await GetMainMenuMessage(userData.ChatId, userData);

                return;
            }

            await _infoStorage.UpdateRecord(userData.UserId, recordId, text);

            await _bot.SendMessage(userData.ChatId.ToString(), $"Запись обновлена.");
            await GetMainMenuMessage(userData.ChatId, userData);
        }

        /// <summary>
        /// Сейчас время - не беспокоить
        /// </summary>
        /// <param name="userData"></param>
        /// <param name="dateTimeUtc"></param>
        /// <returns></returns>
        private static bool InNotDisturbPeriod(UserData userData, DateTime dateTimeUtc)
        {
            if (userData.From == null || userData.To == null || userData.UtcOffset == null)
            {
                return true;
            }

            var time = dateTimeUtc.AddHours(userData.UtcOffset.Value);
            var hour = time.Hour;
            var minute = time.Minute;

            var fromHour = userData.From.Value.Hour;
            var fromMinute = userData.From.Value.Minute;
            var toHour = userData.To.Value.Hour;
            var toMinute = userData.To.Value.Minute;

            if (fromHour == toHour)
            {
                if (fromMinute < toMinute)
                {
                    if (hour == fromHour && minute >= fromMinute && minute < toMinute)
                    {
                        return true;
                    }
                }
                else
                {
                    if (hour == fromHour && minute >= toMinute && minute < fromMinute)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            else if (fromHour < toHour)
            {
                if (hour == fromHour)
                {
                    if (minute >= fromMinute)
                    {
                        return true;
                    }
                }
                else if (hour == toHour)
                {
                    if (minute < toMinute)
                    {
                        return true;
                    }
                }
                else
                {
                    if (hour > fromHour && hour < toHour)
                    {
                        return true;
                    }
                }
            }
            else if (fromHour > toHour)
            {
                if (hour == fromHour)
                {
                    if (minute >= fromMinute)
                    {
                        return true;
                    }
                }
                else if (hour == toHour)
                {
                    if (minute < toMinute)
                    {
                        return true;
                    }
                }
                else
                {
                    if (hour > fromHour || hour < toHour)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ValidSettings(UserData userData)
        {
            return userData?.From != null && userData?.To != null &&
                userData?.Period != null && userData?.UtcOffset != null;
        }

        private async Task GetInformationMenu(Message message, UserData userData)
        {
            if (!ValidSettings(userData))
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Пожалуйста, введите желаемые настройки перед использованием бота");
                await GetMainMenuMessage(message.ChatId, userData);
                return;
            }

            await _bot.CreateKeyboard(message.ChatId.ToString(),
                "Выберите действие", new[] { "Вчера", "Сегодня" }, false, true);
            return;
        }

        private readonly Regex _dateReg = new Regex(@"[0-2]\d:[0-5]\d");
        private readonly Regex _datePeriodReg = new Regex(@"[0-2]\d:[0-5]\d( | - |-)[0-2]\d:[0-5]\d");
        private async Task SetNotDisturb(Message message, UserData userData)
        {
            var matches = _datePeriodReg.Matches(message.Text);
            if (matches.Count == 0)
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Указано недопустимое время. Попробуйте снова");
                await NotDisturbMenu(message, userData);
                return;
            }

            var dateTimeStr = matches[0].Value;
            var dateTimeFromStr = dateTimeStr.Substring(0, 5);
            var dateTimeToStr = dateTimeStr.Substring(dateTimeStr.Length - 5, 5);

            if (!DateTime.TryParse(dateTimeFromStr, out var dateTimeFrom) ||
                !DateTime.TryParse(dateTimeToStr, out var dateTimeTo))
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Указано недопустимое время. Попробуйте снова");
                await NotDisturbMenu(message, userData);
                return;
            }

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.From = dateTimeFrom;
            userDataToUpdate.To = dateTimeTo;
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(), $"Время, когда не стоит вас беспокоить, установлено");
        }
        private async Task SetCheckWordsPeriod(Message message, UserData userData)
        {
            if (!int.TryParse(message.Text, out var count) || count <= 0 || count >= 20 * 60)
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Указано недопустимое количество минут. Попробуйте снова");
                await CheckWordsPeriodMenu(message, userData);
                return;
            }

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.Period = count;
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(), $"Количество минут между итерациями установлено");
        }

        private static async Task SelectRecord(Message message, UserData userData)
        {
            if (userData.PreData.TryGetValue(EAction.SelectRecord, out var data))
            {
                // to do
                return;
            }

            if (data is Dictionary<int, Guid> pairs)
            {

            }
            else
            {
                return;
            }
        }

        private async Task SetTimeZone(Message message, UserData userData)
        {
            var matches = _dateReg.Matches(message.Text);
            if (matches.Count == 0)
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Указано недопустимое время. Попробуйте снова");
                await TimeZoonMenu(message, userData);
                return;
            }

            var dateTimeStr = matches[0].Value;
            if (!DateTime.TryParse(dateTimeStr, out var dateTime))
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Указано недопустимое время. Попробуйте снова");
                await TimeZoonMenu(message, userData);
                return;
            }

            var offsetHour = (dateTime - DateTime.UtcNow).Hours;
            var offsetMinutes = (dateTime - DateTime.UtcNow).TotalMinutes;

            var offset = GetExactlyOffset(offsetHour, offsetMinutes);

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.UtcOffset = offset;
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(), $"Ваш часовой пояс : utc {offset}");
        }

        private static int GetExactlyOffset(int offsetHour, double offsetMinutes)
        {
            var realValue = offsetHour * 60.0;

            var greatValue = (offsetHour + 1) * 60.0;
            var lessValue = (offsetHour - 1) * 60.0;

            if (Math.Abs(Math.Abs(offsetMinutes) - Math.Abs(greatValue))
                < Math.Abs(Math.Abs(offsetMinutes) - Math.Abs(realValue)))
            {
                return offsetHour + 1;
            }
            else if (Math.Abs(Math.Abs(offsetMinutes) - Math.Abs(lessValue))
                < Math.Abs(Math.Abs(offsetMinutes) - Math.Abs(realValue)))
            {
                return offsetHour - 1;
            }

            return offsetHour;
        }

        private async Task SelectRecordMenu(Message message, UserData userData)
        {
            var data = await _infoStorage.GetNewRecords(message.UserId);

            if (data.Count == 0)
            {
                await _bot.SendMessage(userData.ChatId.ToString(), "Не найдено записей");
                await GetMainMenuMessage(message.ChatId, userData);
                return;
            }

            var responseList = new List<string>
            {
                "Список записей : ",
            };

            var idsList = new List<string>();
            var pairs = new Dictionary<int, Guid>();

            var idx = 1;
            foreach (var item in data.OrderBy(x => x.Date))
            {
                var name = item.Data.ToLower();

                var dataItem = $"{idx}. {name}";
                responseList.Add(dataItem);

                idsList.Add(idx.ToString());

                pairs.Add(idx, item.Id);

                idx++;
            }

            var response = string.Join(Environment.NewLine, responseList);
            await _bot.SendMessage(userData.ChatId.ToString(), response);

            await _bot.CreateKeyboard(userData.ChatId.ToString(),
                "Выберите запись", idsList.ToArray(), false, true);

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.Actions.Push(EAction.SelectRecord);
            if (userDataToUpdate.PreData.ContainsKey(EAction.SelectRecord))
            {
                userDataToUpdate.PreData[EAction.SelectRecord] = pairs;
            }
            else
            {
                userDataToUpdate.PreData.TryAdd(EAction.SelectRecord, pairs);
            }

            await SaveUserData(userDataToUpdate);
        }

        private async Task NotDisturbMenu(Message message, UserData userData)
        {
            if (userData.UtcOffset == null)
            {
                await _bot.SendMessage(message.ChatId.ToString(), "Не указан часовой пояс");
                await TimeZoonMenu(message, userData);
                return;
            }

            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.Actions.Push(EAction.SetNotDisturb);
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(),
                "Введите время, когда не стоит вас беспокоить (18:00 - 10:00)");
        }
        private async Task TimeZoonMenu(Message message, UserData userData)
        {
            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.Actions.Push(EAction.SetTimeZone);
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(), "Введите текущее время (00:00)");
        }
        private async Task CheckWordsPeriodMenu(Message message, UserData userData)
        {
            var userDataToUpdate = await GetUserData(userData.UserId);
            userDataToUpdate.Actions.Push(EAction.SetCheckWordsPeriod);
            await SaveUserData(userDataToUpdate);

            await _bot.SendMessage(message.ChatId.ToString(), "Введите период (в минутах)");
        }
        private async Task Settings(Message message, UserData userData)
        {
            await _bot.CreateKeyboard(message.ChatId.ToString(),
                "Выберите действие", new[] { "Время м/у итерациями", "Часовой пояс", "Не беспокоить", "Меню" }, false, true);
        }
        private async Task GetInformation(Message message, UserData userData, DateTime? date)
        {
            var data = date.HasValue 
                ? await _infoStorage.GetArchiveRecords(message.UserId, date.Value)
                : await _infoStorage.GetNewRecords(message.UserId);

            var tasks = new List<string>();

            foreach (var item in data.OrderBy(x => x.Date))
            {
                var name = item.Data.ToLower();

                if (!tasks.Contains(name))
                {
                    tasks.Add(name);
                }
            }

            var responseList = new List<string>
            {
                "Список задач : ",
            };

            if (!tasks.Any())
            {
                responseList.Add("Ничего не делалось.");
            }

            var idx = 1;
            foreach (var item in tasks)
            {
                var dataItem = $"{idx}. {item}";
                responseList.Add(dataItem);

                idx++;
            }

            var response = string.Join(Environment.NewLine, responseList);
            await _bot.SendMessage(userData.ChatId.ToString(), response);

            await GetMainMenuMessage(message.ChatId, userData);
        }

        private async Task GetMainMenuMessage(int chatId, UserData userData)
        {
            await _bot.CreateKeyboard(chatId.ToString(),
                "Выберите действие", new[] { "Что было",  "Настройки" }, false, true);
        }
    }
}