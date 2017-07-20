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

namespace LezetBot
{
    class Program
    {
        public static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        static TelegramBotClient Bot;
        static string BotUsername;

        static Dictionary<int, string> UserKeywords = new Dictionary<int, string>();
        static object lckUserKeywords = new object();

        static Dictionary<int, long> UserChatIDs = new Dictionary<int, long>();
        static object lckUserChatIDs = new object();

        static Dictionary<int, string> Usernames = new Dictionary<int, string>();
        static object lckUsernames = new object();

        static Dictionary<long, List<int>> ChatUsers = new Dictionary<long, List<int>>();
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

            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnMessageEdited += BotOnMessageReceived;
            Bot.OnReceiveError += BotOnReceiveError;

            var me = Bot.GetMeAsync().Result;
            BotUsername = me.Username;
            Console.Title = BotUsername;
            Bot.StartReceiving();

            _quitEvent.WaitOne();

            Bot.StopReceiving();
            SaveData();
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
                    var data = JsonConvert.DeserializeObject<Tuple<Dictionary<int, string>, Dictionary<int, long>, Dictionary<int, string>, Dictionary<long, List<int>>>>(json);
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
                var data = new Tuple<Dictionary<int, string>, Dictionary<int, long>, Dictionary<int, string>, Dictionary<long, List<int>>>(UserKeywords, UserChatIDs, Usernames, ChatUsers);
                System.IO.File.WriteAllText(SaveFile, JsonConvert.SerializeObject(data));
            }
        }

        static string GetKeywords(int uid)
        {
            lock (lckUserKeywords)
            {
                return UserKeywords.ContainsKey(uid) ? UserKeywords[uid] : null;
            }
        }
        static void SetKeywords(int uid, string keywords)
        {
            lock (lckUserKeywords)
            {
                UserKeywords[uid] = keywords;
            }
            SaveData();
        }

        static long GetChatID(int uid)
        {
            lock (lckUserChatIDs)
            {
                return UserChatIDs.ContainsKey(uid) ? UserChatIDs[uid] : long.MinValue;
            }
        }
        static void SetChatID(int uid, long chatid)
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

        static string GetUsername(int uid)
        {
            lock (lckUsernames)
            {
                return Usernames.ContainsKey(uid) ? Usernames[uid] : null;
            }
        }
        static void SetUsername(int uid, string username)
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

        static List<int> GetUsers(long chatid)
        {
            lock (lckChatUsers)
            {
                List<int> result = new List<int>();
                if (ChatUsers.ContainsKey(chatid))
                    result.AddRange(ChatUsers[chatid]);
                return result;
            }
        }
        static void AddUser(long chatid, int uid)
        {
            lock (lckChatUsers)
            {
                if (!ChatUsers.ContainsKey(chatid))
                    ChatUsers[chatid] = new List<int>();
                if (!ChatUsers[chatid].Contains(uid))
                {
                    ChatUsers[chatid].Add(uid);
                    SaveData();
                }
            }
        }
        static void DelUser(long chatid, int uid)
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

        private static async void BotOnMessageReceived(object sender, MessageEventArgs e)
        {
            var msg = e.Message;
            if (msg == null) return;

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

            if (msg.Type != MessageType.TextMessage) return;


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

                List<int> users;
                lock (lckUserKeywords)
                    users = UserKeywords.Keys.ToList();

                foreach (int uid in users)
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

        static async void SendNotification(int uid, string word, Message msg)
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

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs e)
        {
            Log(true, e.ApiRequestException.ToString());

            if (Debugger.IsAttached)
                Debugger.Break();
        }

    }
}