using System.Text;
using System.Linq;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using FinancialSystem.DBMS;

namespace FinancialSystem.Services
{
    class TelegramService
    {
        readonly TelegramBotClient client;

        public TelegramService()
        {
            // Вставляем токен вашего бота в качестве аргумента для конструктора.
            client = new TelegramBotClient("");
            SubscribeEvents();
            client.StartReceiving();
        }

        public async void InitializeAsync()
        {
            await client.GetMeAsync();
        }

        void SubscribeEvents()
        {
            client.OnMessage += BotOnMessage;
            client.OnCallbackQuery += BotOnCallbackQuery;
        }

        async void BotOnCallbackQuery(object sender, CallbackQueryEventArgs callbackQueryEventArgs)
        {
            var callbackQuery = callbackQueryEventArgs.CallbackQuery;

            if (callbackQuery.Data.Contains("Guide"))
            {
                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Никак, пошел нахуй.");
                return;
            }
            await client.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Помощь в использовании: /help" +
                                                      "\nИнициализация в БД чата: /init" +
                                                      "\nЗанять денег: /borrow" +
                                                      "\nПогасить долг: /yield" +                                                      
                                                      "\nОтмена текущих операций: /cancel" +
                                                      "\nИнформация о займах и долгах: /me" +
                                                      "\nПодтвердить займ или погашение долга: /approveUsername"
                                                      );
            return;
        }

        async void BotOnMessage(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.Text)
                return;
            var messageId = message.MessageId;
            var chatId = message.Chat.Id;
            var userId = message.From.Id;
            var userName = message.From.Username;
            var userText = message.Text;

            if (userText.StartsWith("/help"))
            {
                await client.SendTextMessageAsync(chatId, "Выберите один из вариантов:", replyMarkup: new InlineKeyboardMarkup(
            new[]
            {
                        InlineKeyboardButton.WithCallbackData("Список команд", "Commands"),
                        InlineKeyboardButton.WithCallbackData("Как использовать бота", "Guide")
            }));
                return;
            }
            using ApplicationContext db = new ApplicationContext();
            if (userText.StartsWith("/init"))
            {
                if (userName == null)
                {
                    await client.SendTextMessageAsync(chatId, "Чтобы использовать бота, необходимо иметь имя пользователя. Назначьте его в профиле.", replyToMessageId: messageId);
                    return;
                }
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId) == null)
                {
                    Debtor debtor = new Debtor { ChatId = chatId, DebtorId = userId, DebtorPhase = 1, DebtorUsername = userName };
                    db.Debtors.Add(debtor);
                    db.SaveChanges();
                    await client.SendTextMessageAsync(chatId, "Вы добавлены в базу данных комнаты, теперь вы можете пользоваться всем функционалом бота.", replyToMessageId: messageId);
                    return;
                }
                await client.SendTextMessageAsync(chatId, "Вы уже находитесь в базе данных комнаты.", replyToMessageId: messageId);
                return;
            }
            else if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId) == null)
            {
                await client.SendTextMessageAsync(chatId, "Вам нужно добавиться в базу данных комнаты.", replyToMessageId: messageId);
                return;
            }
            else if (userText.StartsWith("/borrow"))
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId != userId) == null)
                {
                    await client.SendTextMessageAsync(chatId, "Не у кого занимать.", replyToMessageId: messageId);
                    return;
                }
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 2 || d.DebtorPhase == 3 || d.DebtorPhase == 4 || d.DebtorPhase == 6 || d.DebtorPhase == 7)) == null)
                {
                    var borrow = db.Debtors.First(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 1);
                    borrow.DebtorPhase = 2;
                    db.SaveChanges();
                    await client.SendTextMessageAsync(chatId, $"Выберите у кого вы хотите занять:", replyToMessageId: messageId, replyMarkup: BorrowKeyboard(chatId, userId, db));
                    return;
                }
                await client.SendTextMessageAsync(chatId, "Сначала завершите предыдущую операцию, либо используйте /cancel", replyToMessageId: messageId);
                return;
            }
            else if (userText.StartsWith("/approve"))
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.LenderUsername == userName && userText.Contains("/approve" + d.DebtorUsername) && d.DebtorPhase == 4) != null)
                {
                    var approveBorrow = db.Debtors.First(d => d.ChatId == chatId && d.LenderUsername == userName && d.DebtorPhase == 4);
                    approveBorrow.DebtorPhase = 5;
                    Debtor debtor = new Debtor { ChatId = chatId, DebtorId = approveBorrow.DebtorId, DebtorPhase = 1, DebtorUsername = approveBorrow.DebtorUsername };
                    db.Debtors.Add(debtor);
                    db.SaveChanges();
                    await client.SendTextMessageAsync(chatId, $"Подтвержден займ для @{approveBorrow.DebtorUsername} на сумму: {approveBorrow.LoanAmount}", replyToMessageId: messageId);
                    return;
                }
                else if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.LenderUsername == userName && userText.Contains("/approve" + d.DebtorUsername) && d.DebtorPhase == 7) != null)
                {
                    var approveYield = db.Debtors.Where(d => d.ChatId == chatId && d.LenderUsername == userName && d.DebtorPhase == 7);
                    ushort loanAmount = 0;
                    foreach (var item in approveYield)
                    {
                        loanAmount += item.LoanAmount;
                        db.Debtors.Remove(item);
                    }
                    Debtor debtor = new Debtor { ChatId = chatId, DebtorId = approveYield.First().DebtorId, DebtorPhase = 1, DebtorUsername = approveYield.First().DebtorUsername };
                    db.Debtors.Add(debtor);
                    await client.SendTextMessageAsync(chatId, $"Подтверждёно погошение займа для @{approveYield.First().DebtorUsername} на сумму: {loanAmount}", replyToMessageId: messageId);
                    db.SaveChanges();
                    return;
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, "Некорректный ввод. Или же, некого подтверждать.", replyToMessageId: messageId);
                    return;
                }
            }
            else if (userText.StartsWith("/yield"))
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 2 || d.DebtorPhase == 3 || d.DebtorPhase == 4 || d.DebtorPhase == 6 || d.DebtorPhase == 7)) == null)
                {
                    if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 5) != null)
                    {
                        var yield = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 5);
                        foreach (var item in yield)
                        {
                            item.DebtorPhase = 6;
                        }
                        db.SaveChanges();
                        await client.SendTextMessageAsync(chatId, "Выберите у кого вы одалживали:", replyToMessageId: messageId, replyMarkup: YieldKeyboard(chatId, userId, db));
                        return;
                    }
                    await client.SendTextMessageAsync(chatId, "Некому возвращать долг.", replyToMessageId: messageId);
                    return;
                }
                await client.SendTextMessageAsync(chatId, "Сначала завершите предыдущую операцию, либо используйте /cancel.", replyToMessageId: messageId);
                return;
            }
            else if (userText.StartsWith("/cancel"))
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase != 1 && d.DebtorPhase != 5) != null)
                {
                    if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 6 || d.DebtorPhase == 7)) != null)
                    {
                        var cancelDebtorPhase6 = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 6 || d.DebtorPhase == 7));
                        foreach (var item in cancelDebtorPhase6)
                        {
                            item.DebtorPhase = 5;
                        }
                        db.SaveChanges();
                    }
                    var cancel = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase != 5);
                    foreach (var item in cancel)
                    {
                        db.Debtors.Remove(item);
                    }
                    Debtor debtor = new Debtor { ChatId = chatId, DebtorId = userId, DebtorPhase = 1, DebtorUsername = userName };
                    db.Debtors.Add(debtor);
                    db.SaveChanges();
                    await client.SendTextMessageAsync(chatId, "Текущая операция отменена.", replyToMessageId: messageId, replyMarkup: new ReplyKeyboardRemove() { Selective = true });
                    return;
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, "Нечего отменять.", replyToMessageId: messageId);
                }
            }
            else if (userText.StartsWith("/me"))
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 5 || d.DebtorPhase == 6 || d.DebtorPhase == 7)) != null)
                {
                    var lenders = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && (d.DebtorPhase == 5 || d.DebtorPhase == 6 || d.DebtorPhase == 7));
                    var descriptionBuilder = new StringBuilder();
                    foreach (var item in lenders)
                    {
                        descriptionBuilder.Append($"{item.LenderUsername} - {item.LoanAmount}\n");
                    }
                    await client.SendTextMessageAsync(chatId, $"История займов {userName}:\n{descriptionBuilder}", replyToMessageId: messageId);
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, "У вас отсутствуют какие-либо займы.", replyToMessageId: messageId);
                }
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.LenderUsername == userName && (d.DebtorPhase == 5 || d.DebtorPhase == 6 || d.DebtorPhase == 7)) != null)
                {
                    var debtors = db.Debtors.Where(d => d.ChatId == chatId && d.LenderUsername == userName && (d.DebtorPhase == 5 || d.DebtorPhase == 6 || d.DebtorPhase == 7));
                    var descriptionBuilder = new StringBuilder();
                    foreach (var item in debtors)
                    {
                        descriptionBuilder.Append($"{item.DebtorUsername} - {item.LoanAmount}\n");
                    }
                    await client.SendTextMessageAsync(chatId, $"Должники {userName}:\n{descriptionBuilder}", replyToMessageId: messageId);
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, "У вас отсутствуют какие-либо должники.", replyToMessageId: messageId);
                }
            }
            else
            {
                if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 2) != null)
                {
                    var lenders = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId != userId);
                    List<string> debtorsUsername = new List<string>();
                    foreach (var lender in lenders)
                    {
                        if (!debtorsUsername.Contains(lender.DebtorUsername))
                        {
                            debtorsUsername.Add(lender.DebtorUsername);
                        }
                    }
                    if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && debtorsUsername.Contains(userText) && d.DebtorPhase == 2) != null)
                    {
                        var borrow = db.Debtors.First(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 2);
                        borrow.LenderUsername = userText;
                        borrow.DebtorPhase = 3;
                        db.SaveChanges();
                        await client.SendTextMessageAsync(chatId, "Выберите сумму:", replyToMessageId: messageId, replyMarkup: new ForceReplyMarkup() { Selective = true });
                        return;
                    }
                    await client.SendTextMessageAsync(chatId, "Некорректный ввод, используйте встроенную клавиатуру.", replyToMessageId: messageId);
                    return;
                }
                else if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 3) != null)
                {
                    var approveBorrow = db.Debtors.First(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 3);
                    if (ushort.TryParse(userText, out ushort result) && result != 0)
                    {
                        approveBorrow.LoanAmount = result;
                        approveBorrow.DebtorPhase = 4;
                        db.SaveChanges();
                        await client.SendTextMessageAsync(chatId, $"От {approveBorrow.LenderUsername} требуется подтвердить займ командой /approve{userName} на сумму: {userText}", replyToMessageId: messageId, replyMarkup: new ReplyKeyboardRemove() { Selective = true });
                        return;
                    }
                    await client.SendTextMessageAsync(chatId, "Некорректный ввод, требуется вводить целые числа со значениями от 1 до 65 535.", replyToMessageId: messageId);
                    await client.SendTextMessageAsync(chatId, "Выберите сумму:", replyToMessageId: messageId, replyMarkup: new ForceReplyMarkup() { Selective = true });
                    return;
                }
                else if (db.Debtors.FirstOrDefault(d => d.ChatId == chatId && d.DebtorId == userId && d.LenderUsername == userText && d.DebtorPhase == 6) != null)
                {
                    var approveYield = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && d.LenderUsername == userText && d.DebtorPhase == 6);
                    ushort loanAmount = 0;
                    foreach (var item in approveYield)
                    {
                        item.DebtorPhase = 7;
                        loanAmount += item.LoanAmount;
                    }
                    await client.SendTextMessageAsync(chatId, $"От {approveYield.First().LenderUsername} требуется подтвердить погошение займа командой /approve{userName} на сумму: {loanAmount}", replyToMessageId: messageId, replyMarkup: new ReplyKeyboardRemove() { Selective = true });
                    db.SaveChanges();
                    var cancelDebtorPhase6 = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && d.DebtorPhase == 6);
                    foreach (var item in cancelDebtorPhase6)
                    {
                        item.DebtorPhase = 5;
                    }
                    db.SaveChanges();
                    return;
                }
                await client.SendTextMessageAsync(chatId, "Некорректный ввод, используйте встроенную клавиатуру.", replyToMessageId: messageId);
                return;
            }
        }

        ReplyKeyboardMarkup BorrowKeyboard(long chatId, int userId, ApplicationContext db)
        {
            var debtors = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId != userId);
            List<string> debtorsUsername = new List<string>();
            foreach (var debtor in debtors)
            {
                if (!debtorsUsername.Contains(debtor.DebtorUsername))
                {
                    debtorsUsername.Add(debtor.DebtorUsername);
                }
            }
            KeyboardButton[] keyboardButtons = new KeyboardButton[debtorsUsername.Count()];
            for (int i = 0; i < debtorsUsername.Count; i++)
            {
                keyboardButtons[i] = new KeyboardButton(debtorsUsername[i]);
            }
            return new ReplyKeyboardMarkup(keyboardButtons) { ResizeKeyboard = true, Selective = true };
        }

        ReplyKeyboardMarkup YieldKeyboard(long chatId, int userId, ApplicationContext db)
        {
            var lenders = db.Debtors.Where(d => d.ChatId == chatId && d.DebtorId == userId && d.LenderUsername != null);
            List<string> lendersUsername = new List<string>();
            foreach (var lender in lenders)
            {
                if (!lendersUsername.Contains(lender.LenderUsername))
                {
                    lendersUsername.Add(lender.LenderUsername);
                }
            }
            KeyboardButton[] keyboardButtons = new KeyboardButton[lendersUsername.Count()];
            for (int i = 0; i < lendersUsername.Count; i++)
            {
                keyboardButtons[i] = new KeyboardButton(lendersUsername[i]);
            }
            return new ReplyKeyboardMarkup(keyboardButtons) { ResizeKeyboard = true, Selective = true };
        }
    }
}
