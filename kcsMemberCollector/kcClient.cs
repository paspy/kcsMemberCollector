using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace kcsMemberCollector {
    public class kcClient {
        private string m_clientName;
        private string m_token;
        private string m_server;
        private string m_exportFileName;
        private string m_logFileName;
        private HttpClient m_client;
        private List<long> m_lstMemberIds;
        public kcClient(string token, string server, long min, long max, string clientName = "defaultClient") {
            m_client = new HttpClient();
            m_clientName = clientName;
            m_token = token;
             m_server = server;
            m_lstMemberIds = new List<long>();
            for (long i = min; i <= max; i++)
                m_lstMemberIds.Add(i);
            var rnd = new Random();
            m_lstMemberIds = m_lstMemberIds.OrderBy(item => rnd.Next()).ToList();
            string serverFolder = server.Replace('.', '_');
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), serverFolder));
            m_exportFileName = Path.Combine(serverFolder, string.Format("{0}-{1}_{2}.txt", m_server.Replace('.', '_'), min, max));
            m_logFileName = Path.Combine(serverFolder, string.Format("{0}.log", m_server.Replace('.', '_')));
            RemoveDuplicateId();
        }

        public kcClient(string token, string server, List<long> memberIds, string clientName = "defaultClient") {
            m_client = new HttpClient();
            m_clientName = clientName;
            m_token = token;
            m_server = server;
            m_lstMemberIds = new List<long>();

            var rnd = new Random();
            m_lstMemberIds = memberIds.OrderBy(item => rnd.Next()).ToList();
            m_exportFileName = string.Format("{0}-lst.txt", m_server.Replace('.', '_'));
            m_logFileName = string.Format("{0}-lst.log", m_server.Replace('.', '_'));
            RemoveDuplicateId();
        }

        HttpRequestMessage CreateHttpRequestMessage(string destUrl, HttpContent content) {
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

        public async Task StartCacheMembersAsync() {
            double count = m_lstMemberIds.Count;
            double current = 0;
            foreach (var memberId in m_lstMemberIds) {
                var memberInfo = await GetMemberInfo(memberId);
                if (memberInfo != null)
                    Log(string.Format("Current Member Id: {0} acquired. {1:f2}%", memberId, (current / count * 100.0)));
                else
                    Log("Current Member Id: " + memberId + " is null.");
                current++;
            }
            Log("RUA.");
        }

        private async Task<string> GetMemberInfo(long memberId) {
            try {

                var postContent = new Dictionary<string, string> {
                        {"api_token", m_token},
                        {"api_member_id",memberId.ToString()},
                        {"api_verno","1"},
                    };

                var httpReqMsg = CreateHttpRequestMessage(string.Format(@"http://{0}/kcsapi/api_req_member/get_practice_enemyinfo", m_server), new FormUrlEncodedContent(postContent));

                var respones = await m_client.SendAsync(httpReqMsg);
                var rawResult = await respones.Content.ReadAsStringAsync();
                var rawJson = rawResult.Substring(7);
                dynamic json = JValue.Parse(rawJson);
                if ((int)json.api_result != 1) {
                    string msg = json.api_result_msg;
                    Log(string.Format("{0}: Error on acquiring member data, code: {1}.", memberId, json.api_result));
                    if ((int)json.api_result == 201) {
                        Environment.Exit(0);
                    }
                    return null;
                }

                using (StreamWriter sw = new StreamWriter(new FileStream(m_exportFileName, FileMode.Append), Encoding.UTF8)) {
                    sw.WriteLine(rawJson);
                }

                return rawJson;
            } catch (Exception e) {
                Log("Exception on GetMember: " + memberId);
                Log(e.Message);
                return null;
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
                                dynamic json = JValue.Parse(line);
                                long id = (long)json.api_data.api_member_id;
                                if (!m_lstMemberIds.Remove(id)) {
                                    Log("Remove ID error: " + id);
                                }
                            }
                        }
                    }
                }
            } catch (Exception e) {
                Log("RemoveDuplicateId Exception: " + e.Message);
                throw;
            }
            Log("Total removed duplicated IDs: " + (prevCount - m_lstMemberIds.Count));
        }

        public void Log(string log) {
            string s = log + " - by " + m_clientName;
            Console.WriteLine(s);
            using (StreamWriter sw = new StreamWriter(new FileStream(m_logFileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)) {
                sw.WriteLine(string.Format("[{0}] {1}", DateTime.UtcNow.ToString(), s));
            }
            
        }
    }
}
