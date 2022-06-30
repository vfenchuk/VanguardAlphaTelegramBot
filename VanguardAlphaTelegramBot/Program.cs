using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Extensions.LoginWidget;
using Telegram.Bot.Passport;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Options;
using System.Text.RegularExpressions;
using Quartz;
using System.Collections.Specialized;
using Quartz.Impl;
using System.Linq;
using System.Text;

namespace VanguardAlphaTelegramBot {
	static class Program {
		public class Attendance {
			public string Id { get; set; }
			public DateTime AnswerDateTime { get; set; }
			public Raider Raider { get; set; }
			public bool IsPresent { get; set; }
		}
		public class Character {
			public string Name { get; set; }
			public string Class { get; set; }
		}
		public class Raider {
			public string Id { get; set; }
			public Character Character { get; set; }
			public int DailyAskTimeHours { get; set; }
			public int DailyAskTimeMinutes { get; set; }
			public string RealName { get; set; }
		}
		public enum BotStateEnum {
			DefaultReset = 0,
			Initialization = 1,
			RegistrationNickname = 2,
			RegistrationClass = 3,
			RegistrationRealName = 4,
			RegistrationDailyAskTime = 5,
			DailySubscribed = 6,
			Asked = 7,
			ChangeAskTime = 8
		}
		public class VanguardBotHelper {
			public string Id { get; set; }
			public Dictionary<string, BotStateEnum> ChatStates { get; set; }
			public Dictionary<string, BotStateEnum> ReadStates() {
				IMongoDatabase vanguardDatabase = _mongoDbClient.GetDatabase(ConstVanguardMongoDbDatabaseName);
				IMongoCollection<VanguardBotHelper> collection = vanguardDatabase.GetCollection<VanguardBotHelper>(ConstBotStatesCollection);
				ChatStates = collection.Find(Builders<VanguardBotHelper>.Filter.Empty).FirstOrDefault().ChatStates;
				return ChatStates;
			}
			public void UpdateStates(Dictionary<string, BotStateEnum> chatStates) {
				IMongoDatabase vanguardDatabase = _mongoDbClient.GetDatabase(ConstVanguardMongoDbDatabaseName);
				IMongoCollection<VanguardBotHelper> collection = vanguardDatabase.GetCollection<VanguardBotHelper>(ConstBotStatesCollection);
				UpdateDefinition<VanguardBotHelper> updateDefinition = Builders<VanguardBotHelper>.Update.Set(x => x.ChatStates, chatStates);
				UpdateOptions upsertOptions = new UpdateOptions() { IsUpsert = true };
				collection.UpdateOne(Builders<VanguardBotHelper>.Filter.Empty, updateDefinition, upsertOptions);
			}
		}

		const string ConstVanguardMongoDbConnectionString = "mongodb://localhost:27017/?serverSelectionTimeoutMS=5000&connectTimeoutMS=10000";
		const string ConstVanguardMongoDbDatabaseName = "vanguard";
		const string ConstBotStatesCollection = "BotStatesCollection";
		const string ConstRaidersCollection = "RaidersCollection";
		const string ConstAttendanceCollection = "AttendanceCollection";
		static MongoClient _mongoDbClient = new MongoClient(ConstVanguardMongoDbConnectionString);
		static VanguardBotHelper _botHelper = new VanguardBotHelper();
		static ITelegramBotClient bot = new TelegramBotClient("5581855045:AAGxadel0WB7c61C6me0a2KgHcCr-pprLdY");

		public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {

			//Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(update));

			if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message) {
				Message message = update.Message;
				string chatId = message.Chat.Id.ToString();
				message.Text = message.Text.Trim();

				_botHelper.ReadStates();
				if (!_botHelper.ChatStates.ContainsKey(chatId)) {
					_botHelper.ChatStates.Add(chatId, BotStateEnum.Initialization);
					_botHelper.UpdateStates(_botHelper.ChatStates);
				}

				ReplyKeyboardMarkup askTimeKeyboardMarkup = new(new[] {
								new KeyboardButton[] { "20:00 Вечера" },
								new KeyboardButton[] { "19:00 Вечера" },
								new KeyboardButton[] { "17:00 Дня" },
								new KeyboardButton[] { "15:00 Дня" },
								new KeyboardButton[] { "13:00 Дня" },
								new KeyboardButton[] { "11:00 Утра" }
							}) { ResizeKeyboard = true };

				ReplyKeyboardMarkup dailySubscribedKeyboardMarkup = new(new[] {
								new KeyboardButton[] { "Показать сегодняшний состав" },
								new KeyboardButton[] { "Изменить время опроса" },
							}) { ResizeKeyboard = true };

				ReplyKeyboardMarkup askKeyboardMarkup = new(new[] {
								new KeyboardButton[] { "Буду вовремя" },
								new KeyboardButton[] { "Опоздаю на 15 минут" },
								new KeyboardButton[] { "Опоздаю на 60 минут, и стану на замену" },
								new KeyboardButton[] { "Меня сегодня совсем не будет, нужна замена" },

							}) { ResizeKeyboard = true };

				IMongoDatabase vanguardDatabase = _mongoDbClient.GetDatabase(ConstVanguardMongoDbDatabaseName);
				IMongoCollection<Raider> raiderCollection = vanguardDatabase.GetCollection<Raider>(ConstRaidersCollection);
				IMongoCollection<Attendance> attendanceCollection = vanguardDatabase.GetCollection<Attendance>(ConstAttendanceCollection);


				switch (_botHelper.ChatStates[chatId]) {
					case BotStateEnum.DefaultReset:
					case BotStateEnum.Initialization:
						if (message.Text.ToLower() == "/start") {
							ReplyKeyboardMarkup registerKeyboardMarkup = new(new[] { new KeyboardButton[] { "/register" }, }) { ResizeKeyboard = true };

							await botClient.SendTextMessageAsync(
								chatId: message.Chat,
								text: "Здарова ЛС! Валентин хочет чтобы ты зарегистрировался. /register",
								replyMarkup: registerKeyboardMarkup,
								cancellationToken: cancellationToken);
							return;
						}
						if (message.Text.ToLower() == "/register") {
							_botHelper.ChatStates[chatId] = BotStateEnum.RegistrationNickname;
							_botHelper.UpdateStates(_botHelper.ChatStates);
							await botClient.SendTextMessageAsync(message.Chat, "Напиши мне никнейм своего main персонажа, которым ходишь в рейд", replyMarkup: new ReplyKeyboardRemove());
							return;
						}
						break;
					case BotStateEnum.RegistrationNickname:
						Regex nickNameRegex = new Regex(@"([А-Яа-я])\w+");
						string nickname = "";
						if (nickNameRegex.IsMatch(message.Text) && message.Text.Length <= 12 && message.Text.Length >= 3) {
							nickname = Capitalize(message.Text.ToLower());

							if (raiderCollection.Find(Builders<Raider>.Filter.Eq(x => x.Character.Name, nickname)).FirstOrDefault() == null) {
								Raider newRaider = new Raider() {
									Id = chatId,
									Character = new Character() {
										Name = nickname
									}
								};
								raiderCollection.InsertOne(newRaider);
								await botClient.SendTextMessageAsync(message.Chat, $"Отлично. Запишу {nickname} в список ЛСов. Продолжим");
							} else {
								await botClient.SendTextMessageAsync(message.Chat, $"Твой персонаж уже зарегистрирован.\nБот все про тебя знает.\n");
							}

							_botHelper.ChatStates[chatId] = BotStateEnum.RegistrationClass;
							_botHelper.UpdateStates(_botHelper.ChatStates);
							await botClient.SendTextMessageAsync(message.Chat, "Напиши мне класс этого персонажа. Например: Охотник");
						} else {
							await botClient.SendTextMessageAsync(message.Chat, "Таких никнеймов не бывает, попробуй еще раз\n\nНапиши мне никнейм своего main персонажа, которым ходишь в рейд");
							return;
						}
						break;
					case BotStateEnum.RegistrationClass:
						List<string> knownClasses = new List<string>();
						knownClasses.Add("Воин");
						knownClasses.Add("Паладин");
						knownClasses.Add("Охотник");
						knownClasses.Add("Разбойник");
						knownClasses.Add("Жрец");
						knownClasses.Add("Шаман");
						knownClasses.Add("Маг");
						knownClasses.Add("Чернокнижник");
						knownClasses.Add("Монах");
						knownClasses.Add("Друид");
						knownClasses.Add("Охотник на демонов");
						knownClasses.Add("Рыцарь смерти");
						knownClasses.Add("Эвокер");

						if (message.Text.ToLower().Contains("варлок")) {
							message.Text = "Чернокнижник";
						} else if (message.Text.ToLower().Contains("вар")) {
							message.Text = "Воин";
						} else if (message.Text.ToLower().Contains("лок")) {
							message.Text = "Чернокнижник";
						} else if (message.Text.ToLower().Contains("рог")) {
							message.Text = "Разбойник";
						} else if (message.Text.ToLower().Contains("пал")) {
							message.Text = "Паладин";
						} else if (message.Text.ToLower().Contains("прист")) {
							message.Text = "Жрец";
						} else if (message.Text.ToLower().Contains("дру")) {
							message.Text = "Друид";
						} else if (message.Text.ToLower().Contains("дх")) {
							message.Text = "Охотник на демонов";
						} else if (message.Text.ToLower().Contains("дк")) {
							message.Text = "Рыцарь смерти";
						} else if (message.Text.ToLower().Contains("монк")) {
							message.Text = "Монах";
						} else if (message.Text.ToLower().Contains("хант")) {
							message.Text = "Охотник";
						} else if (message.Text.ToLower().Contains("вокер")) {
							message.Text = "Эвокер";
						}

						string сlassName = Capitalize(message.Text.ToLower());

						if (knownClasses.Contains(сlassName)) {
							UpdateDefinition<Raider> classUpdateDefinition = Builders<Raider>.Update.Set(x => x.Character.Class, сlassName);
							UpdateOptions upsertOptions = new UpdateOptions() { IsUpsert = true };
							raiderCollection.UpdateOne(Builders<Raider>.Filter.Eq(x => x.Id, chatId), classUpdateDefinition);

							await botClient.SendTextMessageAsync(message.Chat, $"Отлично. Твой класс {сlassName}. Продолжим");
							_botHelper.ChatStates[chatId] = BotStateEnum.RegistrationRealName;
							_botHelper.UpdateStates(_botHelper.ChatStates);
							await botClient.SendTextMessageAsync(message.Chat, "Напиши мне свое реальное имя. Например: Валентин");
						} else {
							await botClient.SendTextMessageAsync(message.Chat, "Таких класов в игре нет, я то шарю, попробуй еще раз\n\nНапиши мне класс этого персонажа. Например: Чернокнижник");
							return;
						}
						break;
					case BotStateEnum.RegistrationRealName:
						Regex realNameRegex = new Regex(@"([А-Яа-я])\w+");
						string realName = "";

						if (message.Text.ToLower().Contains("лёха")) {
							message.Text = "Алексей";
						} else if (message.Text.ToLower().Contains("валера")) {
							message.Text = "Валерий";
						} else if (message.Text.ToLower().Contains("серега")) {
							message.Text = "Сергей";
						} else if (message.Text.ToLower().Contains("настя")) {
							message.Text = "Анастасия";
						} else if (message.Text.ToLower().Contains("паша")) {
							message.Text = "Павел";
						} else if (message.Text.ToLower().Contains("макс")) {
							message.Text = "Максим";
						} else if (message.Text.ToLower().Contains("костя")) {
							message.Text = "Константин";
						} else if (message.Text.ToLower().Contains("вова")) {
							message.Text = "Владимир";
						} else if (message.Text.ToLower().Contains("ваня")) {
							message.Text = "Иван";
						} else if (message.Text.ToLower().Contains("витя")) {
							message.Text = "Виктор";
						} else if (message.Text.ToLower().Contains("боря")) {
							message.Text = "Борис";
						} else if (message.Text.ToLower().Contains("саша")) {
							message.Text = "Александр";
						} else if (message.Text.ToLower().Contains("юра")) {
							message.Text = "Юрий";
						} else if (message.Text.ToLower().Contains("андрюсик")) {
							message.Text = "Андрюсик";
							await botClient.SendTextMessageAsync(message.Chat, $"Андрюсик? знаем такого, сынуле привет от всей гильдии");
						}

						if (realNameRegex.IsMatch(message.Text)) {
							realName = Capitalize(message.Text.ToLower());

							//UPDATE DB
							UpdateDefinition<Raider> realNameUpdateDefinition = Builders<Raider>.Update.Set(x => x.RealName, realName);
							raiderCollection.UpdateOne(Builders<Raider>.Filter.Eq(x => x.Id, chatId), realNameUpdateDefinition);

							await botClient.SendTextMessageAsync(message.Chat, $"Отлично. Тебя зовут {realName}. Продолжим");
							_botHelper.ChatStates[chatId] = BotStateEnum.RegistrationDailyAskTime;
							_botHelper.UpdateStates(_botHelper.ChatStates);
							await botClient.SendTextMessageAsync(message.Chat, "В дни когда у нас РТ, моя задача спрашивать тебя о посещении сегодняшнего рейда.");
							await botClient.SendTextMessageAsync(
								chatId: message.Chat,
								text: "Во сколько тебе удобно будет отвечать?",
								replyMarkup: askTimeKeyboardMarkup,
								cancellationToken: cancellationToken);
							return;
						} else {
							await botClient.SendTextMessageAsync(message.Chat, "Не знаю такого имени, ты точно человек? попробуй еще раз\n\nНапиши мне свое реальное имя. Например: Тимофей");
							return;
						}
						break;
					case BotStateEnum.RegistrationDailyAskTime:
						int dailyAskTimeHours = 20;
						int dailyAskTimeMinutes = 0;
						switch (message.Text) {
							case "11:00 Утра":
								dailyAskTimeHours = 11;
								dailyAskTimeMinutes = 00;
								break;
							case "13:00 Дня":
								dailyAskTimeHours = 13;
								dailyAskTimeMinutes = 00;
								break;
							case "15:00 Дня":
								dailyAskTimeHours = 15;
								dailyAskTimeMinutes = 00;
								break;
							case "17:00 Дня":
								dailyAskTimeHours = 17;
								dailyAskTimeMinutes = 00;
								break;
							case "19:00 Вечера":
								dailyAskTimeHours = 19;
								dailyAskTimeMinutes = 00;
								break;
							case "20:00 Вечера":
								dailyAskTimeHours = 20;
								dailyAskTimeMinutes = 00;
								break;
							default:
								await botClient.SendTextMessageAsync(
									chatId: message.Chat,
									text: "Во сколько тебе удобно будет отвечать?",
									replyMarkup: askTimeKeyboardMarkup,
									cancellationToken: cancellationToken);
								return;

						}
						//UPDATE DB
						UpdateDefinition<Raider> AskTimeUpdateDefinition = Builders<Raider>.Update.Set(x => x.DailyAskTimeHours, dailyAskTimeHours).Set(x => x.DailyAskTimeMinutes, dailyAskTimeMinutes);
						raiderCollection.UpdateOne(Builders<Raider>.Filter.Eq(x => x.Id, chatId), AskTimeUpdateDefinition);
						_botHelper.ChatStates[chatId] = BotStateEnum.DailySubscribed;
						_botHelper.UpdateStates(_botHelper.ChatStates);

						await botClient.SendTextMessageAsync(
							chatId: message.Chat,
							text: "Чем могу помочь?",
							replyMarkup: dailySubscribedKeyboardMarkup,
							cancellationToken: cancellationToken);
						return;
						break;
					case BotStateEnum.DailySubscribed:
						switch (message.Text) {
							case "Изменить время опроса":
								_botHelper.ChatStates[chatId] = BotStateEnum.ChangeAskTime;
								_botHelper.UpdateStates(_botHelper.ChatStates);
								await botClient.SendTextMessageAsync(
									chatId: message.Chat,
									text: "Во сколько тебе удобно будет отвечать?",
									replyMarkup: askTimeKeyboardMarkup,
									cancellationToken: cancellationToken);
								break;
							case "Показать сегодняшний состав":
								if (DateTime.Now.DayOfWeek != DayOfWeek.Monday && DateTime.Now.DayOfWeek != DayOfWeek.Wednesday && DateTime.Now.DayOfWeek != DayOfWeek.Thursday) {
									await botClient.SendTextMessageAsync(message.Chat, "Сегодня нет рейда. Пожалуйста отдохни нам нужны все силы в следующем рейде!");
									return;
								}

								StringBuilder sb = new StringBuilder("Состав на сегодня:");

								FilterDefinition<Attendance> attendanceFilterDefinition = Builders<Attendance>.Filter.Lte(x => x.AnswerDateTime, DateTime.Now.Date.AddDays(1).AddTicks(-1)) &
																							Builders<Attendance>.Filter.Gte(x => x.AnswerDateTime, DateTime.Now.Date) &
																							Builders<Attendance>.Filter.Where(x => x.IsPresent);
								List<Attendance> attendances = attendanceCollection.Find(attendanceFilterDefinition).ToList();

								List<string> alreadyInIds = new List<string>();

								#region Tanks
								List<Attendance> tankAttendances = new List<Attendance>();
								foreach (Attendance attendanceWho in attendances) {
									if (attendanceWho.Raider.Character.Name == "Этичность" || attendanceWho.Raider.Character.Name == "Кейдорри") {
										if (alreadyInIds.Contains(attendanceWho.Raider.Id)) {
											continue;
										} else {
											alreadyInIds.Add(attendanceWho.Raider.Id);
											tankAttendances.Add(attendanceWho);
										}
									}
									if (tankAttendances.Count < 2) {
										Attendance standinTank = attendances.Where(x => x.Raider.Character.Name == "Мангус").FirstOrDefault();
										if (standinTank != null) {
											if (alreadyInIds.Contains(standinTank.Raider.Id)) {
												continue;
											} else {
												alreadyInIds.Add(standinTank.Raider.Id);
												tankAttendances.Add(standinTank);
											}

										}
									}
								}
								sb.Append("\n" + $"  Танки [{tankAttendances.Count}]");
								foreach (Attendance tankAttendance in tankAttendances) {
									sb.Append("\n    " + GetNameStr(tankAttendance.Raider));
								}
								#endregion

								#region Heals
								List<Attendance> healAttendances = new List<Attendance>();
								foreach (Attendance attendanceWho in attendances) {
									if (attendanceWho.Raider.Character.Name == "Глобгор" || attendanceWho.Raider.Character.Name == "Евиае" || attendanceWho.Raider.Character.Name == "Найлини" || attendanceWho.Raider.Character.Name == "Илиссия" || attendanceWho.Raider.Character.Name == "Оверхам") {
										if (alreadyInIds.Contains(attendanceWho.Raider.Id)) {
											continue;
										} else {
											alreadyInIds.Add(attendanceWho.Raider.Id);
											healAttendances.Add(attendanceWho);
										}

									}
								}
								sb.Append("\n" + $"  Хилы [{healAttendances.Count}]");
								foreach (Attendance healAttendance in healAttendances) {
									sb.Append("\n    " + GetNameStr(healAttendance.Raider));
								}
								#endregion

								#region Dps
								List<Attendance> dpsAttendances = new List<Attendance>();
								foreach (Attendance attendanceWho in attendances) {
									if (alreadyInIds.Contains(attendanceWho.Raider.Id)) {
										continue;
									} else {
										alreadyInIds.Add(attendanceWho.Raider.Id);
										dpsAttendances.Add(attendanceWho);
									}
								}
								sb.Append("\n" + $"  Дамагеры [{dpsAttendances.Count}]");
								foreach (Attendance dpsAttendance in dpsAttendances) {
									sb.Append("\n    " + GetNameStr(dpsAttendance.Raider));
								}
								#endregion
								await botClient.SendTextMessageAsync(message.Chat, sb.ToString());
								break;
							default:
								await botClient.SendTextMessageAsync(
								chatId: message.Chat,
								text: "Чем могу помочь?",
								replyMarkup: dailySubscribedKeyboardMarkup,
								cancellationToken: cancellationToken);
								return;
						}
						break;
					case BotStateEnum.Asked:
						bool isPresent = false;
						switch (message.Text) {
							case "Буду вовремя":
								await botClient.SendTextMessageAsync(message.Chat, "Супер, за Авангард!", replyMarkup: dailySubscribedKeyboardMarkup);
								isPresent = true;
								break;
							case "Опоздаю на 15 минут":
								await botClient.SendTextMessageAsync(message.Chat, "Ок, постарайся не опоздать. Будем ждать", replyMarkup: dailySubscribedKeyboardMarkup);
								isPresent = true;
								break;
							case "Опоздаю на 60 минут, и стану на замену":
								await botClient.SendTextMessageAsync(message.Chat, "Ок, будем ждать, зайдешь на стрим как будешь на месте", replyMarkup: dailySubscribedKeyboardMarkup);
								isPresent = false;
								break;
							case "Меня сегодня совсем не будет, нужна замена":
								await botClient.SendTextMessageAsync(message.Chat, "Ок, не беспокойся. Хорошо отдохни", replyMarkup: dailySubscribedKeyboardMarkup);
								isPresent = false;
								break;
							default:
								await bot.SendTextMessageAsync(
								chatId: chatId,
								text: "Когда сегодня будешь?",
								replyMarkup: askKeyboardMarkup);
								return;
						}

						Raider raider = raiderCollection.Find(Builders<Raider>.Filter.Eq(x => x.Id, chatId)).FirstOrDefault();
						Attendance attendance = new Attendance() {
							Id = Guid.NewGuid().ToString(),
							Raider = raider,
							AnswerDateTime = DateTime.Now,
							IsPresent = isPresent
						};
						attendanceCollection.InsertOne(attendance);

						_botHelper.ChatStates[chatId] = BotStateEnum.DailySubscribed;
						_botHelper.UpdateStates(_botHelper.ChatStates);
						break;
					case BotStateEnum.ChangeAskTime:
						dailyAskTimeHours = 20;
						dailyAskTimeMinutes = 0;
						switch (message.Text) {
							case "11:00 Утра":
								dailyAskTimeHours = 11;
								dailyAskTimeMinutes = 00;
								break;
							case "13:00 Дня":
								dailyAskTimeHours = 13;
								dailyAskTimeMinutes = 00;
								break;
							case "15:00 Дня":
								dailyAskTimeHours = 15;
								dailyAskTimeMinutes = 00;
								break;
							case "17:00 Дня":
								dailyAskTimeHours = 17;
								dailyAskTimeMinutes = 00;
								break;
							case "19:00 Вечера":
								dailyAskTimeHours = 19;
								dailyAskTimeMinutes = 00;
								break;
							case "20:00 Вечера":
								dailyAskTimeHours = 20;
								dailyAskTimeMinutes = 00;
								break;
							default:
								await botClient.SendTextMessageAsync(
									chatId: message.Chat,
									text: "Во сколько тебе удобно будет отвечать?",
									replyMarkup: askTimeKeyboardMarkup,
									cancellationToken: cancellationToken);
								return;

						}
						//UPDATE DB
						AskTimeUpdateDefinition = Builders<Raider>.Update.Set(x => x.DailyAskTimeHours, dailyAskTimeHours).Set(x => x.DailyAskTimeMinutes, dailyAskTimeMinutes);
						raiderCollection.UpdateOne(Builders<Raider>.Filter.Eq(x => x.Id, chatId), AskTimeUpdateDefinition);
						_botHelper.ChatStates[chatId] = BotStateEnum.DailySubscribed;
						_botHelper.UpdateStates(_botHelper.ChatStates);

						await botClient.SendTextMessageAsync(
							chatId: message.Chat,
							text: "Спасибо, дальше просто отвечай на мои запросы в назначеное время.\n\nЧем могу помочь?",
							replyMarkup: dailySubscribedKeyboardMarkup,
							cancellationToken: cancellationToken);
						return;
						break;
				}
			}
		}


		public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
			string message = $"Exception message: {exception.Message}";
			Console.WriteLine(message);
			await botClient.SendTextMessageAsync(421450283, message);
		}
		internal class AskJob : IJob {
			public async Task Execute(IJobExecutionContext context) {
				IMongoDatabase vanguardDatabase = _mongoDbClient.GetDatabase(ConstVanguardMongoDbDatabaseName);
				IMongoCollection<VanguardBotHelper> helperCollection = vanguardDatabase.GetCollection<VanguardBotHelper>(ConstBotStatesCollection);

				Dictionary<string, BotStateEnum> сhatStates = helperCollection.Find(Builders<VanguardBotHelper>.Filter.Empty).FirstOrDefault().ChatStates;
				List<string> activeUserChatIds = сhatStates.Where(x => x.Value == BotStateEnum.DailySubscribed).Select(x => x.Key).ToList();

				IMongoCollection<Raider> raiderCollection = vanguardDatabase.GetCollection<Raider>(ConstRaidersCollection);
				List<Raider> activeRaiders = raiderCollection.Find(Builders<Raider>.Filter.In(x => x.Id, activeUserChatIds)).ToList();

				List<Raider> raidersToAsk = activeRaiders.Where(x => x.DailyAskTimeHours == context.ScheduledFireTimeUtc.Value.DateTime.Hour && x.DailyAskTimeMinutes == context.ScheduledFireTimeUtc.Value.DateTime.Minute).ToList();

				ReplyKeyboardMarkup askKeyboardMarkup = new(new[] {
								new KeyboardButton[] { "Буду вовремя" },
								new KeyboardButton[] { "Опоздаю на 15 минут" },
								new KeyboardButton[] { "Опоздаю на 60 минут, и стану на замену" },
								new KeyboardButton[] { "Меня сегодня совсем не будет, нужна замена" },

							}) { ResizeKeyboard = true };

				foreach (Raider raider in raidersToAsk) {
					_botHelper.ChatStates[raider.Id] = BotStateEnum.Asked;
					_botHelper.UpdateStates(_botHelper.ChatStates);
					await bot.SendTextMessageAsync(
								chatId: raider.Id,
								text: "Когда сегодня будешь?",
								replyMarkup: askKeyboardMarkup);
				}

				await Task.CompletedTask;
			}
		}

		static async Task TestScheduler() {
			// construct a scheduler factory
			NameValueCollection props = new NameValueCollection {
				{ "quartz.serializer.type", "binary" }
			};
			StdSchedulerFactory factory = new StdSchedulerFactory(props);

			// get a scheduler
			IScheduler sched = await factory.GetScheduler();
			await sched.Start();

			// define the job and tie it to our HelloJob class
			IJobDetail nextWednesday11Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday11Job", "MainRaid").Build();
			IJobDetail nextWednesday13Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday13Job", "MainRaid").Build();
			IJobDetail nextWednesday15Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday15Job", "MainRaid").Build();
			IJobDetail nextWednesday17Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday17Job", "MainRaid").Build();
			IJobDetail nextWednesday19Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday19Job", "MainRaid").Build();
			IJobDetail nextWednesday20Job = JobBuilder.Create<AskJob>().WithIdentity("nextWednesday20Job", "MainRaid").Build();
			IJobDetail nextThursday11Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday11Job", "MainRaid").Build();
			IJobDetail nextThursday13Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday13Job", "MainRaid").Build();
			IJobDetail nextThursday15Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday15Job", "MainRaid").Build();
			IJobDetail nextThursday17Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday17Job", "MainRaid").Build();
			IJobDetail nextThursday19Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday19Job", "MainRaid").Build();
			IJobDetail nextThursday20Job = JobBuilder.Create<AskJob>().WithIdentity("nextThursday20Job", "MainRaid").Build();
			IJobDetail nextMonday11Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday11Job", "MainRaid").Build();
			IJobDetail nextMonday13Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday13Job", "MainRaid").Build();
			IJobDetail nextMonday15Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday15Job", "MainRaid").Build();
			IJobDetail nextMonday17Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday17Job", "MainRaid").Build();
			IJobDetail nextMonday19Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday19Job", "MainRaid").Build();
			IJobDetail nextMonday20Job = JobBuilder.Create<AskJob>().WithIdentity("nextMonday20Job", "MainRaid").Build();

			IJobDetail testJob = JobBuilder.Create<AskJob>().WithIdentity("testJob", "TestRaid").Build();

			int daysUntilWednesday = ((int) DayOfWeek.Wednesday - (int) DateTime.Now.DayOfWeek + 7) % 7;
			DateTime nextWednesday = DateTime.Now.AddDays(daysUntilWednesday);
			DateTimeOffset nextWednesday11 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 11, 00, 00, 00);
			DateTimeOffset nextWednesday13 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 13, 00, 00, 00);
			DateTimeOffset nextWednesday15 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 15, 00, 00, 00);
			DateTimeOffset nextWednesday17 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 17, 00, 00, 00);
			DateTimeOffset nextWednesday19 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 19, 00, 00, 00);
			DateTimeOffset nextWednesday20 = new DateTime(nextWednesday.Year, nextWednesday.Month, nextWednesday.Day, 20, 00, 00, 00);


			int daysUntilThursday = ((int) DayOfWeek.Thursday - (int) DateTime.Now.DayOfWeek + 7) % 7;
			DateTime nextThursday = DateTime.Now.AddDays(daysUntilThursday);
			DateTimeOffset nextThursday11 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 11, 00, 00, 00);
			DateTimeOffset nextThursday13 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 13, 00, 00, 00);
			DateTimeOffset nextThursday15 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 15, 00, 00, 00);
			DateTimeOffset nextThursday17 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 17, 00, 00, 00);
			DateTimeOffset nextThursday19 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 19, 00, 00, 00);
			DateTimeOffset nextThursday20 = new DateTime(nextThursday.Year, nextThursday.Month, nextThursday.Day, 20, 00, 00, 00);

			int daysUntilMonday = ((int) DayOfWeek.Monday - (int) DateTime.Now.DayOfWeek + 7) % 7;
			DateTime nextMonday = DateTime.Now.AddDays(daysUntilMonday);
			DateTimeOffset nextMonday11 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 11, 00, 00, 00);
			DateTimeOffset nextMonday13 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 13, 00, 00, 00);
			DateTimeOffset nextMonday15 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 15, 00, 00, 00);
			DateTimeOffset nextMonday17 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 17, 00, 00, 00);
			DateTimeOffset nextMonday19 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 19, 00, 00, 00);
			DateTimeOffset nextMonday20 = new DateTime(nextMonday.Year, nextMonday.Month, nextMonday.Day, 20, 00, 00, 00);

			ITrigger nextWednesday11Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday11Trigger", "WednesdayTriggers").StartAt(nextWednesday11).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextWednesday13Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday13Trigger", "WednesdayTriggers").StartAt(nextWednesday13).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextWednesday15Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday15Trigger", "WednesdayTriggers").StartAt(nextWednesday15).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextWednesday17Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday17Trigger", "WednesdayTriggers").StartAt(nextWednesday17).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextWednesday19Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday19Trigger", "WednesdayTriggers").StartAt(nextWednesday19).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextWednesday20Trigger = TriggerBuilder.Create().WithIdentity("nextWednesday20Trigger", "WednesdayTriggers").StartAt(nextWednesday20).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday11Trigger = TriggerBuilder.Create().WithIdentity("nextThursday11Trigger", "ThursdayTriggers").StartAt(nextThursday11).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday13Trigger = TriggerBuilder.Create().WithIdentity("nextThursday13Trigger", "ThursdayTriggers").StartAt(nextThursday13).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday15Trigger = TriggerBuilder.Create().WithIdentity("nextThursday15Trigger", "ThursdayTriggers").StartAt(nextThursday15).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday17Trigger = TriggerBuilder.Create().WithIdentity("nextThursday17Trigger", "ThursdayTriggers").StartAt(nextThursday17).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday19Trigger = TriggerBuilder.Create().WithIdentity("nextThursday19Trigger", "ThursdayTriggers").StartAt(nextThursday19).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextThursday20Trigger = TriggerBuilder.Create().WithIdentity("nextThursday20Trigger", "ThursdayTriggers").StartAt(nextThursday20).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday11Trigger = TriggerBuilder.Create().WithIdentity("nextMonday11Trigger", "MondayTriggers").StartAt(nextMonday11).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday13Trigger = TriggerBuilder.Create().WithIdentity("nextMonday13Trigger", "MondayTriggers").StartAt(nextMonday13).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday15Trigger = TriggerBuilder.Create().WithIdentity("nextMonday15Trigger", "MondayTriggers").StartAt(nextMonday15).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday17Trigger = TriggerBuilder.Create().WithIdentity("nextMonday17Trigger", "MondayTriggers").StartAt(nextMonday17).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday19Trigger = TriggerBuilder.Create().WithIdentity("nextMonday19Trigger", "MondayTriggers").StartAt(nextMonday19).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();
			ITrigger nextMonday20Trigger = TriggerBuilder.Create().WithIdentity("nextMonday20Trigger", "MondayTriggers").StartAt(nextMonday20).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();

			//ITrigger testTrigger = TriggerBuilder.Create().WithIdentity("TestTrigger", "WednesdayTriggers").StartAt(DateTime.Now.AddSeconds(10)).WithSimpleSchedule(x => x.WithIntervalInHours(168).RepeatForever()).Build();

			await sched.ScheduleJob(nextWednesday11Job, nextWednesday11Trigger);
			await sched.ScheduleJob(nextWednesday13Job, nextWednesday13Trigger);
			await sched.ScheduleJob(nextWednesday15Job, nextWednesday15Trigger);
			await sched.ScheduleJob(nextWednesday17Job, nextWednesday17Trigger);
			await sched.ScheduleJob(nextWednesday19Job, nextWednesday19Trigger);
			await sched.ScheduleJob(nextWednesday20Job, nextWednesday20Trigger);
			await sched.ScheduleJob(nextThursday11Job, nextThursday11Trigger);
			await sched.ScheduleJob(nextThursday13Job, nextThursday13Trigger);
			await sched.ScheduleJob(nextThursday15Job, nextThursday15Trigger);
			await sched.ScheduleJob(nextThursday17Job, nextThursday17Trigger);
			await sched.ScheduleJob(nextThursday19Job, nextThursday19Trigger);
			await sched.ScheduleJob(nextThursday20Job, nextThursday20Trigger);
			await sched.ScheduleJob(nextMonday11Job, nextMonday11Trigger);
			await sched.ScheduleJob(nextMonday13Job, nextMonday13Trigger);
			await sched.ScheduleJob(nextMonday15Job, nextMonday15Trigger);
			await sched.ScheduleJob(nextMonday17Job, nextMonday17Trigger);
			await sched.ScheduleJob(nextMonday19Job, nextMonday19Trigger);
			await sched.ScheduleJob(nextMonday20Job, nextMonday20Trigger);

			await sched.ScheduleJob(testJob, testTrigger);
		}


		static async Task Main() {
			Console.WriteLine("Запущен бот " + bot.GetMeAsync().Result.FirstName);
			_botHelper.ReadStates();
			var cts = new CancellationTokenSource();
			var cancellationToken = cts.Token;
			var receiverOptions = new ReceiverOptions {
				AllowedUpdates = { }, // receive all update types
			};
			bot.StartReceiving(
				HandleUpdateAsync,
				HandleErrorAsync,
				receiverOptions,
				cancellationToken
			);

			await TestScheduler();

			Console.ReadLine();
		}

		#region Static Helpers
		public static string Capitalize(this string s) {
			if (String.IsNullOrEmpty(s)) {
				throw new ArgumentException("String is null or empty");
			}

			return s[0].ToString().ToUpper() + s.Substring(1);
		}

		public static string GetNameStr(Raider raider) {
			return raider.Character.Name + $" ({raider.RealName})";
		}
		#endregion
	}
}

