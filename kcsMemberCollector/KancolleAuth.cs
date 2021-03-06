﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Web;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kcsMemberCollector {

    public sealed class KancolleAccount {
        public int ServerId { get; set; }
        public string ServerName { get; set; }
        public string LoginId { get; set; }
        public string ServerAddress { get; set; }
        public string LoginPassword { get; set; }
        public string Token { get; set; }
    }

    public sealed class KancolleAccessInfo {
        public int ServerId { get; set; }
        public string ServerAddress { get; set; }
        public string Token { get; set; }
    }

    public sealed class KancolleAuth {

        private readonly ILogger<KancolleAuth> m_logger;

        private HttpClient m_client;
        private string m_loginId;
        private string m_password;
        private KancolleAccessInfo m_kcAccessInfo;
        private bool m_isInit = false;
        public KancolleAuth(ILogger<KancolleAuth> logger) {
            m_logger = logger;
        }

        public void Initialize(string loginId, string pwd) {
            m_client = new HttpClient(new HttpClientHandler() {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true,
            });
            m_client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            m_client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko");
            m_loginId = loginId;
            m_password = pwd;
            m_isInit = true;
            m_logger.LogInformation("KancolleAuth has been initialized. Current DMM LoginID: {0}", loginId);
        }

        public async Task<KancolleAccessInfo> GetKancolleAccessInfo(bool forceUpdate = false) {
            if (!m_isInit) throw new Exception("Class Not Initialized.");

            if (m_kcAccessInfo != null && !forceUpdate) return m_kcAccessInfo;
            var dmmTokens = await GetDMMTokensForAjaxAsync();
            var ajaxTokens = await GetAjaxTokensAsync(dmmTokens.Item1, dmmTokens.Item2);
            var osApiUrl = await GetOSAPIUrlAsync(ajaxTokens.Item1, ajaxTokens.Item2, ajaxTokens.Item3);
            var serverToken = await GetKancolleServerTokenAsync(osApiUrl);
            m_kcAccessInfo = new KancolleAccessInfo() {
                ServerId = serverToken.Item1,
                ServerAddress = serverToken.Item2,
                Token = serverToken.Item3
            };
            return m_kcAccessInfo;
        }

        private async Task<Tuple<string, string>> GetDMMTokensForAjaxAsync() {
            try {
                var response = await m_client.GetAsync(AuthURLs["login"]);
                var htmlResult = await response.Content.ReadAsStringAsync();

                var dmm_tokenResult = AuthPattens["dmm_token"].Match(htmlResult);
                if (!dmm_tokenResult.Success) {
                    m_logger.LogError("Get dmm_token result failed.");
                    throw new Exception("Get dmm_token result failed.");
                }
                var tokenResult = AuthPattens["token"].Match(htmlResult);
                if (!tokenResult.Success) {
                    m_logger.LogError("Get token result failed.");
                    throw new Exception("Get token result failed.");
                }

                //Console.WriteLine(dmm_tokenResult.Value);
                //Console.WriteLine(tokenResult.Value);
                var dmm_token = dmm_tokenResult.Value.Substring(25, 32);
                var token = tokenResult.Value.Substring(16, 32);
                //var dmm_token = JsonConvert.DeserializeObject<Dictionary<string, string>>(string.Format("{{{0}}}", dmm_tokenResult.Value).Replace(',', ':'));
                //var token = JsonConvert.DeserializeObject<Dictionary<string, string>>(string.Format("{{{0}}}", tokenResult.Value));
                return new Tuple<string, string>(dmm_token, token);

            } catch (Exception e) {
                m_logger.LogError(e, "Exception on GetDMMTokensAjax failed.");
                throw;
            }
        }

        private async Task<Tuple<string, string, string>> GetAjaxTokensAsync(string dmm_token, string token) {
            try {

                using (var httpReqMsg = new HttpRequestMessage()) {
                    httpReqMsg.RequestUri = new Uri(AuthURLs["ajax"]);
                    httpReqMsg.Method = HttpMethod.Post;
                    httpReqMsg.Headers.Add("Origin", "https://accounts.dmm.com");
                    httpReqMsg.Headers.Add("http-dmm-token", dmm_token);
                    httpReqMsg.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    httpReqMsg.Headers.Referrer = new Uri(AuthURLs["login"]);
                    var postData = new Dictionary<string, string> { { "token", token } };
                    httpReqMsg.Content = new FormUrlEncodedContent(postData);

                    var response = await m_client.SendAsync(httpReqMsg);
                    var jsonResult = await response.Content.ReadAsStringAsync();
                    //var ajaxTokens = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResult);
                    dynamic ajaxTokens = JToken.Parse(jsonResult);
                    string aJaxtoken = ajaxTokens.body.token;
                    string ajaxLoginId = ajaxTokens.body.login_id;
                    string ajaxPassword = ajaxTokens.body.password;
                    //return new Tuple<string, string, string>(ajaxTokens["token"], ajaxTokens["login_id"], ajaxTokens["password"]);
                    return new Tuple<string, string, string>(aJaxtoken, ajaxLoginId, ajaxPassword);

                }
            } catch (Exception e) {
                m_logger.LogError(e, "Exception on GetAjaxTokens failed. ");
                throw;
            }
        }

        private async Task<string> GetOSAPIUrlAsync(string token, string idKey, string pwdKey) {
            try {
                using (var httpReqMsg = new HttpRequestMessage()) {
                    httpReqMsg.RequestUri = new Uri(AuthURLs["auth"]);
                    httpReqMsg.Method = HttpMethod.Post;
                    httpReqMsg.Headers.Add("Origin", "https://accounts.dmm.com");
                    httpReqMsg.Headers.Referrer = new Uri(AuthURLs["login"]);
                    var postData = new Dictionary<string, string> {
                        { "token", token },
                        { "login_id", m_loginId },
                        { "password", m_password },
                        { "idKey", m_loginId },
                        { "pwdKey", m_password }
                    };
                    httpReqMsg.Content = new FormUrlEncodedContent(postData);
                    var postResponse1 = await m_client.SendAsync(httpReqMsg);
                    var htmlContent1 = await postResponse1.Content.ReadAsStringAsync();
                    var pwdResetTest = AuthPattens["reset"].Match(htmlContent1);
                    //if need pwd reset
                    if (pwdResetTest.Success) {
                        throw new Exception("DMM needs you to reset password!");
                    }
                }
                var postResponse2 = await m_client.GetAsync(AuthURLs["game"]);
                var htmlContent2 = await postResponse2.Content.ReadAsStringAsync();
                var osapiReslt = AuthPattens["osapi"].Match(htmlContent2);
                if (!osapiReslt.Success) {
                    throw new Exception("DMM User ID or Password Wrong! Please try again.");
                }
                var osapiUrl = new Regex("\"[^\"]*\"").Match(osapiReslt.Value).Value.Trim(new char[] { '"' });
                return osapiUrl;

            } catch (Exception e) {
                m_logger.LogError(e, "Exception on GetOSAPIUrl failed. ");
                throw;
            }
        }

        private async Task<Tuple<int, string, string>> GetKancolleServerTokenAsync(string osapiUrl) {
            try {
                var qs = HttpUtility.ParseQueryString(osapiUrl);
                var owner = qs["owner"];
                var st = qs["st"].Split('#').First();
                var nextUrl = string.Format(AuthURLs["get_world"], owner, DateTimeOffset.Now.ToUnixTimeMilliseconds());
                int worldId = -1;
                string worldIP = string.Empty;
                string apiToken = string.Empty;
                using (var httpReqMsg = new HttpRequestMessage()) {
                    httpReqMsg.RequestUri = new Uri(nextUrl);
                    httpReqMsg.Method = HttpMethod.Get;
                    httpReqMsg.Headers.Add("Origin", "https://www.dmm.com");
                    httpReqMsg.Headers.Referrer = new Uri(osapiUrl);
                    var postResponse = await m_client.SendAsync(httpReqMsg);
                    var jsonResult = await postResponse.Content.ReadAsStringAsync();
                    jsonResult = jsonResult.Substring(7);
                    dynamic svdata = JToken.Parse(jsonResult);
                    if (svdata.api_result == 1) {
                        worldId = svdata.api_data.api_world_id;
                        worldIP = App.WorldServerAddr[worldId - 1];
                    } else
                        throw new Exception("Request world info error.");
                }

                using (var httpReqMsg = new HttpRequestMessage()) {
                    var flashUrl = string.Format(AuthURLs["get_flash"], worldIP, owner, DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    httpReqMsg.RequestUri = new Uri(AuthURLs["make_request"]);
                    httpReqMsg.Method = HttpMethod.Post;
                    httpReqMsg.Headers.Add("Origin", "https://www.dmm.com");
                    httpReqMsg.Headers.Referrer = new Uri(osapiUrl);
                    var postData = new Dictionary<string, string> {
                        {"url", flashUrl},
                        {"httpMethod", "GET"},
                        {"authz", "signed"},
                        {"st", st},
                        {"contentType", "JSON"},
                        {"numEntries", "3"},
                        {"getSummaries", "false"},
                        {"signOwner", "true"},
                        {"signViewer", "true"},
                        {"gadget", "http://203.104.209.7/gadget.xml"},
                        {"container", "dmm"}
                    };
                    httpReqMsg.Content = new FormUrlEncodedContent(postData);
                    var response = m_client.SendAsync(httpReqMsg).Result;
                    var resultByte = response.Content.ReadAsByteArrayAsync().Result;
                    var result = Encoding.UTF8.GetString(resultByte);
                    result = result.Substring(27);
                    dynamic svdata = JToken.Parse(result);

                    if (svdata[flashUrl].rc != 200)
                        throw new Exception();
                    svdata = JToken.Parse(((string)svdata[flashUrl].body).Substring(7));

                    if (svdata.api_result == 1) {
                        apiToken = svdata.api_token;
                    } else
                        throw new Exception("Error code: " + (int)svdata.api_result);
                }

                return new Tuple<int, string, string>(worldId, worldIP, apiToken);
            } catch (Exception e) {
                m_logger.LogError(e, "Exception on GetOSAPIUrl failed. " + e.Message);
                throw;
            }
        }

        ~KancolleAuth() {
            m_client.Dispose();
        }

        public static readonly Dictionary<string, Regex> AuthPattens = new Dictionary<string, Regex>() {
            //  patterns = {'dmm_token': re.compile(r'http-dmm-token" content="([\d|\w]+)"'),
            {"dmm_token", new Regex("http-dmm-token\" content=\"([\\d|\\w]+)\"", RegexOptions.Compiled) },
            //  'token': re.compile(r'token" content="([\d|\w]+)"'),
            {"token", new Regex("token\" content=\"([\\d|\\w]+)\"", RegexOptions.Compiled) },
            {"reset", new Regex(@"認証エラー", RegexOptions.Compiled) },
            {"osapi", new Regex("URL\\W+:\\W+\"(.*)\",", RegexOptions.Compiled) }
        };

        public static readonly Dictionary<string, string> AuthURLs = new Dictionary<string, string>() {
            { "login",          "https://accounts.dmm.com/service/login/password/=/"},
            { "ajax",           "https://accounts.dmm.com/service/api/get-token/"},
            { "auth",           "https://accounts.dmm.com/service/login/password/authenticate/"},
            { "game",           "http://www.dmm.com/netgame/social/-/gadgets/=/app_id=854854/"},
            { "make_request",   "http://osapi.dmm.com/gadgets/makeRequest"},
            { "get_world",      "http://203.104.209.7/kcsapi/api_world/get_id/{0}/1/{1}"},
            { "get_flash",      "http://{0}/kcsapi/api_auth_member/dmmlogin/{1}/1/{2}"},
            { "flash",          "http://{0}/kcs/mainD2.swf?api_token={1}&amp;api_starttime={2}"}
        };

    }


}
