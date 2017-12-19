using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace kcsMemberCollector {
    public class Collector {
        //private readonly Logger m_logger = LogManager.GetLogger("Collector");
        private static Random s_RND = new Random();

        private string m_clientName;
        private string m_token;
        private string m_server;
        private string m_exportFileName;
        private string m_logFileName;
        private bool m_isFormated;
        private HttpClient m_client;
        private List<long> m_lstMemberIds;
        public Collector(string token, string server, long min, long max, string serverName, string clientName = "default", bool format = true) {
            m_isFormated = format;
            m_client = new HttpClient();
            m_clientName = clientName;
            m_token = token;
            m_server = server;
            m_lstMemberIds = new List<long>();
            for (long i = min; i <= max; i++)
                m_lstMemberIds.Add(i);
            m_lstMemberIds = m_lstMemberIds.OrderBy(item => s_RND.Next()).ToList();
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), serverName));
            if (m_isFormated)
                m_exportFileName = Path.Combine(serverName, string.Format("{0}-{1}_formatted.json", min, max));
            else
                m_exportFileName = Path.Combine(serverName, string.Format("{0}-{1}.json", min, max));

            m_logFileName = Path.Combine(serverName, "Collector.log");
            if (!m_isFormated)
                RemoveDuplicateId();
        }

        public async Task StartCacheMembersAsync() {
            double count = m_lstMemberIds.Count;
            double current = 0;
            if (m_isFormated) {
                using (StreamWriter sw = new StreamWriter(new FileStream(m_exportFileName, FileMode.Create), Encoding.UTF8)) {
                    sw.WriteLine("[");
                }
            }
            foreach (var memberId in m_lstMemberIds) {
                var infoResult = await GetMemberInfo(memberId);
                if (infoResult)
                    Utils.Log(string.Format("Current Member Id: {0} acquired. {1:f2}%", memberId, (current / count * 100.0)), m_logFileName, m_clientName);

                current++;
            }
            if (m_isFormated) {

                using (var fileStream = new FileStream(m_exportFileName, FileMode.Open)) {
                    fileStream.Seek(0, SeekOrigin.End);
                    // Hack way to delete last two characters, file must be UTF-8 (\n\r = 2 bytes, \r = 1 byte)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        fileStream.SetLength(fileStream.Length - 3);
                    else
                        fileStream.SetLength(fileStream.Length - 2);
                    using (StreamWriter sw = new StreamWriter(fileStream, Encoding.UTF8)) {
                        sw.WriteLine("]");
                    }
                }
            }
            Utils.Log("RUA.", m_logFileName, m_clientName);
        }

        private async Task<bool> GetMemberInfo(long memberId) {
            try {
                var postContent = new Dictionary<string, string> {
                        {"api_token", m_token},
                        {"api_member_id",memberId.ToString()},
                        {"api_verno","1"},
                    };

                using (var httpReqMsg = CreateHttpRequestMessage(
                    string.Format(@"http://{0}/kcsapi/api_req_member/get_practice_enemyinfo", m_server),
                    new FormUrlEncodedContent(postContent))) {

                    var respones = await m_client.SendAsync(httpReqMsg);
                    var rawResult = await respones.Content.ReadAsStringAsync();
                    var rawJson = rawResult.Substring(7);
                    dynamic json = JToken.Parse(rawJson);
                    if ((int)json.api_result != 1) {
                        Utils.Log(string.Format("{0}: Error on acquiring member data, code: {1}.", memberId, json.api_result), m_logFileName, m_clientName);
                        if ((int)json.api_result == 201) {
                            Environment.Exit(0);
                        }
                        return false;
                    }

                    using (StreamWriter sw = new StreamWriter(new FileStream(m_exportFileName, FileMode.Append), Encoding.UTF8)) {
                        if (m_isFormated) {
                            dynamic jsonData = json.api_data;
                            KCData kcdata = new KCData();

                            kcdata.Nickname = jsonData.api_nickname;
                            kcdata.Nickname = Utils.Base64Encode(kcdata.Nickname);
                            kcdata.Comment = jsonData.api_cmt;
                            kcdata.Comment = Utils.Base64Encode(kcdata.Comment);
                            kcdata.Deckname = jsonData.api_deckname;
                            kcdata.Deckname = Utils.Base64Encode(kcdata.Deckname);

                            kcdata.Member_id = jsonData.api_member_id;
                            kcdata.Rank = jsonData.api_rank;
                            kcdata.Level = jsonData.api_level;
                            kcdata.Exp = jsonData.api_experience[0];
                            kcdata.Friend_count = jsonData.api_friend;
                            kcdata.Ship_current = jsonData.api_ship[0];
                            kcdata.Ship_max = jsonData.api_ship[1];
                            kcdata.Item_current = jsonData.api_slotitem[0];
                            kcdata.Item_max = jsonData.api_slotitem[1];
                            kcdata.Furniture_count = jsonData.api_furniture;
                            kcdata.DeckList = new List<DeckShip>();
                            dynamic ships = jsonData.api_deck.api_ships;
                            foreach (dynamic ship in ships) {
                                if (ship.api_id < 0) continue;
                                var deckship = new DeckShip();
                                deckship.Ship_level = ship.api_level;
                                deckship.Ship_id = ship.api_ship_id;
                                deckship.Ship_star = ship.api_star;
                                kcdata.DeckList.Add(deckship);
                            }
                            kcdata.Update = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            string formatedJson = JsonConvert.SerializeObject(kcdata, Formatting.None) + ",";
                            await sw.WriteLineAsync(formatedJson);
                        } else
                            await sw.WriteLineAsync(rawJson);
                    }
                }

                return true;
            } catch (Exception e) {
                Utils.Log("Exception on GetMember: " + memberId, m_logFileName, m_clientName);
                Utils.Log(e.Message, m_logFileName, m_clientName);
                return false;
            }
        }

        private void RemoveDuplicateId() {
            var filesPath = Directory.GetFiles(Directory.GetCurrentDirectory());
            int prevCount = m_lstMemberIds.Count;
            try {
                foreach (var filePath in filesPath) {
                    string file = Path.GetFileName(filePath);
                    if (file.Equals(m_exportFileName)) {
                        using (StreamReader sr = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read), Encoding.UTF8)) {
                            string line;
                            while ((line = sr.ReadLine()) != null) {
                                dynamic json = JToken.Parse(line);
                                long id = (long)json.api_data.api_member_id;
                                if (!m_lstMemberIds.Remove(id)) {
                                    Utils.Log("Remove ID error: " + id, m_logFileName, m_clientName);
                                }

                            }
                        }
                    }
                }
            } catch (Exception e) {
                Utils.Log("RemoveDuplicateId Exception: " + e.Message, m_logFileName, m_clientName);
                throw;
            }
            Utils.Log("Total removed duplicated IDs: " + (prevCount - m_lstMemberIds.Count), m_logFileName, m_clientName);
        }

        private HttpRequestMessage CreateHttpRequestMessage(string destUrl, HttpContent content) {
            var msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Post;
            msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            msg.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Android; U; zh-CN) AppleWebKit/533.19.4 (KHTML, like Gecko) AdobeAIR/21.0 rqxbjmdizgzp");
            msg.Headers.ExpectContinue = false;
            msg.Headers.Referrer = new Uri("app:/AppMain.swf/[[DYNAMIC]]/1");
            msg.Headers.Add("x-flash-version", "21,0,0,174");
            msg.RequestUri = new Uri(destUrl);
            msg.Content = content;
            return msg;
        }
    }

    public static class Utils {
        private static object locker = new object();

        public static string Base64Encode(string plainText) {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }
        public static string Base64Decode(string base64EncodedData) {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

        public static void Log(string log, string outputPath, string belongs, ConsoleColor fgc = ConsoleColor.Gray, ConsoleColor bgc = ConsoleColor.Black) {
            lock (locker) {
                using (StreamWriter sw = new StreamWriter(new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)) {
                    string s = string.Format("[{0}] {1} - {2}", DateTime.UtcNow.ToString(), log, belongs);
                    string head = string.Format("[{0}] ", DateTime.UtcNow.ToString());
                    //Console.BackgroundColor = ConsoleColor.Black;
                    //Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(head);
                    //Console.BackgroundColor = bgc;
                    //Console.ForegroundColor = fgc;
                    Console.WriteLine(log + " - " + belongs);
                    //Console.BackgroundColor = ConsoleColor.Black;
                    //Console.ForegroundColor = ConsoleColor.Gray;
                    sw.WriteLine(s);
                }
            }
        }
    }

}
