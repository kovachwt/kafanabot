using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;

namespace LezetBot
{
    class Program
    {
        public static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        static TelegramBotClient Bot;
        static string BotUsername;

        static Dictionary<long, string> UserKeywords = new Dictionary<long, string>();
        static object lckUserKeywords = new object();

        static Dictionary<long, long> UserChatIDs = new Dictionary<long, long>();
        static object lckUserChatIDs = new object();

        static Dictionary<long, string> Usernames = new Dictionary<long, string>();
        static object lckUsernames = new object();

        static Dictionary<long, List<long>> ChatUsers = new Dictionary<long, List<long>>();
        static object lckChatUsers = new object();

        static string SaveFile;
        static object lckSavefile = new object();

        static string LogFile;
        static object lckLogFile = new object();

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.CancelKeyPress += (sender, eArgs) => {
                _quitEvent.Set();
                eArgs.Cancel = true;
            };

            string appfolder = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + Path.DirectorySeparatorChar;
            LogFile = appfolder + "kafanabot.log";
            SaveFile = appfolder + "kafanabot.json";
            LoadSaved();

            string apiKey = System.IO.File.ReadAllText(appfolder + "api.key").Trim();
            Bot = new TelegramBotClient(apiKey);

            /*
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnMessageEdited += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;
            */

            var me = Bot.GetMeAsync().Result;
            BotUsername = me.Username;
            Console.Title = BotUsername;


            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = new UpdateType[] {
                UpdateType.CallbackQuery,
                //UpdateType.ChannelPost,
                //UpdateType.ChatJoinRequest,
                UpdateType.ChatMember,
                UpdateType.ChosenInlineResult, 
                //UpdateType.EditedChannelPost, 
                UpdateType.EditedMessage,
                UpdateType.InlineQuery,
                UpdateType.Message,
                UpdateType.MyChatMember,
                UpdateType.Poll,
                UpdateType.PollAnswer, 
                //UpdateType.PreCheckoutQuery, 
                //UpdateType.ShippingQuery 
            }
            };

            Bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );

            _quitEvent.WaitOne();

            cts.Cancel();

            SaveData();
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = update.Type switch
            {
                // UpdateType.Unknown:
                // UpdateType.ChannelPost:
                // UpdateType.EditedChannelPost:
                // UpdateType.ShippingQuery:
                // UpdateType.PreCheckoutQuery:
                // UpdateType.Poll:
                UpdateType.Message => BotOnMessageReceived(botClient, update.Message!),
                UpdateType.EditedMessage => BotOnMessageReceived(botClient, update.EditedMessage!),
                UpdateType.ChatMember => BotOnChatMember(botClient, update.ChatMember!),
                //UpdateType.CallbackQuery => BotOnCallbackQueryReceived(botClient, update.CallbackQuery!),
                //UpdateType.InlineQuery => BotOnInlineQueryReceived(botClient, update.InlineQuery!),
                //UpdateType.ChosenInlineResult => BotOnChosenInlineResultReceived(botClient, update.ChosenInlineResult!),
                _ => UnknownUpdateHandlerAsync(botClient, update)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

        private static Task UnknownUpdateHandlerAsync(ITelegramBotClient botClient, Update update)
        {
            Log(false, $"Unhandled update type {update.Type}, serialized=" + JsonConvert.SerializeObject(update));
            return Task.CompletedTask;
        }

        static void Log(bool iserror, string text)
        {
            string logtxt = (iserror ? "ERROR: " : "") + text;
            Console.WriteLine(logtxt);
            lock (lckLogFile)
                System.IO.File.AppendAllText(LogFile, logtxt + Environment.NewLine);
        }

        static void LoadSaved()
        {
            lock (lckSavefile)
            {
                if (System.IO.File.Exists(SaveFile))
                {
                    string json = System.IO.File.ReadAllText(SaveFile);
                    var data = JsonConvert.DeserializeObject<Tuple<Dictionary<long, string>, Dictionary<long, long>, Dictionary<long, string>, Dictionary<long, List<long>>>>(json);
                    if (data.Item1 != null)
                        UserKeywords = data.Item1;
                    if (data.Item2 != null)
                        UserChatIDs = data.Item2;
                    if (data.Item3 != null)
                        Usernames = data.Item3;
                    if (data.Item4 != null)
                        ChatUsers = data.Item4;
                }
            }
        }

        static void SaveData()
        {
            lock (lckSavefile)
            {
                var data = new Tuple<Dictionary<long, string>, Dictionary<long, long>, Dictionary<long, string>, Dictionary<long, List<long>>>(UserKeywords, UserChatIDs, Usernames, ChatUsers);
                System.IO.File.WriteAllText(SaveFile, JsonConvert.SerializeObject(data));
            }
        }

        static string GetKeywords(long uid)
        {
            lock (lckUserKeywords)
            {
                return UserKeywords.ContainsKey(uid) ? UserKeywords[uid] : null;
            }
        }
        static void SetKeywords(long uid, string keywords)
        {
            lock (lckUserKeywords)
            {
                UserKeywords[uid] = keywords;
            }
            SaveData();
        }

        static long GetChatID(long uid)
        {
            lock (lckUserChatIDs)
            {
                return UserChatIDs.ContainsKey(uid) ? UserChatIDs[uid] : long.MinValue;
            }
        }
        static void SetChatID(long uid, long chatid)
        {
            lock (lckUserChatIDs)
            {
                if (!UserChatIDs.ContainsKey(uid) || UserChatIDs[uid] != chatid)
                {
                    UserChatIDs[uid] = chatid;
                    SaveData();
                }
            }
        }

        static string GetUsername(long uid)
        {
            lock (lckUsernames)
            {
                return Usernames.ContainsKey(uid) ? Usernames[uid] : null;
            }
        }
        static void SetUsername(long uid, string username)
        {
            lock (lckUsernames)
            {
                if (!Usernames.ContainsKey(uid) || Usernames[uid] != username)
                {
                    Usernames[uid] = username;
                    SaveData();
                }
            }
        }

        static List<long> GetUsers(long chatid)
        {
            lock (lckChatUsers)
            {
                List<long> result = new List<long>();
                if (ChatUsers.ContainsKey(chatid))
                    result.AddRange(ChatUsers[chatid]);
                return result;
            }
        }
        static void AddUser(long chatid, long uid)
        {
            lock (lckChatUsers)
            {
                if (!ChatUsers.ContainsKey(chatid))
                    ChatUsers[chatid] = new List<long>();
                if (!ChatUsers[chatid].Contains(uid))
                {
                    ChatUsers[chatid].Add(uid);
                    SaveData();
                }
            }
        }
        static void DelUser(long chatid, long uid)
        {
            lock (lckChatUsers)
            {
                if (ChatUsers.ContainsKey(chatid) && ChatUsers[chatid].Contains(uid))
                {
                    ChatUsers[chatid].Remove(uid);
                    SaveData();
                }
            }
        }

        public static async Task BotOnChatMember(ITelegramBotClient botClient, ChatMemberUpdated update)
        {
            Log(false, "ChatMemberUPDATE: " + JsonConvert.SerializeObject(update));

            if (update.OldChatMember != null && update.OldChatMember.Status == ChatMemberStatus.Left
                || update.NewChatMember != null && update.NewChatMember.Status == ChatMemberStatus.Left)
            {
                await SendShockedGif(update.Chat);
            }
        }

        private static async Task SendShockedGif(Chat chat)
        {
            string gifurl = "https://media2.giphy.com/media/l0NgR62Ooi7ftNlHq/giphy.gif";
            InputOnlineFile file = new InputOnlineFile(new Uri(gifurl));
            await Bot.SendAnimationAsync(chat.Id, file);
        }

        public static async Task BotOnMessageReceived(ITelegramBotClient botClient, Message msg)
        {
            if (msg == null) return;

            if (msg.LeftChatMember != null)
                await SendShockedGif(msg.Chat);

            if (Debugger.IsAttached)
            {
                Log(false, msg.Date.ToString("yyyy-MM-dd HH:mm:ss ") + msg.From.FirstName + ": " + msg.Text);
                Log(false, "JSON= " + JsonConvert.SerializeObject(msg));
            }

            if (msg.Chat.Type == ChatType.Private)
                SetChatID(msg.From.Id, msg.Chat.Id);

            if (!String.IsNullOrEmpty(msg.From.Username))
                SetUsername(msg.From.Id, msg.From.Username);

            if (msg.Chat.Type != ChatType.Private)
            {
                AddUser(msg.Chat.Id, msg.From.Id);
                if (msg.LeftChatMember != null)
                    DelUser(msg.Chat.Id, msg.LeftChatMember.Id);
                if (msg.NewChatMembers != null && msg.NewChatMembers.Count() > 0)
                    foreach (User u in msg.NewChatMembers)
                        AddUser(msg.Chat.Id, u.Id);
            }

            if (msg.Type != MessageType.Text) return;


            string text = msg.Text.ToLower();
            //Console.WriteLine(msg.Date.ToString("HH:mm:ss ") + msg.From.Username + ": " + text);

            bool msgtobot = false;
            if (text.StartsWith("@" + BotUsername.ToLower() + " "))
            {
                text = text.Substring(BotUsername.Length + 1).Trim();
                msgtobot = true;
            }

            if (msg.Chat.Type == ChatType.Private || msgtobot)
            {
                if (text.StartsWith("/keywords "))
                {
                    string keywords = text.Substring("/keywords ".Length);

                    var punctuation = keywords.Where(Char.IsPunctuation).Distinct().ToArray();
                    var words = keywords.Split().Select(x => x.Trim(punctuation)).Where(w => w.Trim() != "");

                    keywords = String.Join(" ", words.Distinct().ToArray());

                    SetKeywords(msg.From.Id, keywords);
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "Done, new keywords are: " + keywords);
                }
                else if (text.StartsWith("/delkeywords"))
                {
                    SetKeywords(msg.From.Id, "");
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "Done, no more keywords.");
                }
                else if (text.StartsWith("/showkeywords"))
                {
                    string keywords = GetKeywords(msg.From.Id);
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "Your keywords are: " + keywords);
                }
                else
                {
                    var usage = @"Usage:
/keywords       - send a new list of notification keywords (list of keywords separated by spaces)
/delkeywords    - empty your list of notification keywords (no more notifications)
/showkeywords   - show your current notification keywords
";
                    await Bot.SendTextMessageAsync(msg.Chat.Id, usage);
                }

                if (!UserChatIDs.ContainsKey(msg.From.Id))
                    SetChatID(msg.From.Id, msg.From.Id);

                return;
            }

            if (msg.Chat.Type != ChatType.Private)
            {
                var punctuation = text.Where(Char.IsPunctuation).Distinct().ToArray();
                var words = text.Split().Select(x => x.Trim(punctuation)).Where(x => x.Trim() != "");
                if (words.Count() == 0) return;

                var chatusers = GetUsers(msg.Chat.Id);

                List<long> users;
                lock (lckUserKeywords)
                    users = UserKeywords.Keys.ToList();

                foreach (long uid in users)
                {
                    if (msg.From.Id == uid) continue;
                    if (!chatusers.Contains(uid)) continue;

                    string username = GetUsername(uid);
                    if (!String.IsNullOrEmpty(username) && text.Contains("@" + username.ToLower()))
                        continue;

                    string keywords = GetKeywords(uid);
                    if (keywords.Trim() == "")
                        continue;
                    foreach (string keyword in keywords.Split())
                        if (words.Contains(keyword) || words.Where(w => w.StartsWith(keyword)).Count() > 0)
                        {
                            SendNotification(uid, keyword, msg);
                            break;
                        }
                }
            }
        }

        static async void SendNotification(long uid, string word, Message msg)
        {
            Log(false, "Sending notification for " + word + " to " + uid + ", text: " + msg.Text);
            long chatid = GetChatID(uid);
            if (chatid == long.MinValue)
            {
                chatid = uid;
                Log(true, "Can't find chat id for uid " + uid + " to send notification...");
            }
            await Bot.ForwardMessageAsync(chatid, msg.Chat.Id, msg.MessageId);
        }

        public static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Log(true, ErrorMessage);

            if (Debugger.IsAttached)
                Debugger.Break();

            return Task.CompletedTask;
        }

    }
}