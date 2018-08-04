using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Collections.Generic;

using Newtonsoft.Json;

using ChargeBee.Exceptions;
using System.Threading.Tasks;

namespace ChargeBee.Api
{
    public static class ApiUtil
    {
        private static DateTime m_unixTime = new DateTime(1970, 1, 1);

        public static string BuildUrl(params string[] paths)
        {
            StringBuilder sb = new StringBuilder(ApiConfig.Instance.ApiBaseUrl);

            foreach (var path in paths)
            {
                sb.Append('/').Append(WebUtility.UrlEncode(path));
            }

            return sb.ToString();
        }

        private static HttpWebRequest GetRequest(string url, HttpMethod method, Dictionary<string, string> headers, ApiConfig env)
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = Enum.GetName(typeof(HttpMethod), method);
            //request.UserAgent = String.Format("ChargeBee-DotNet-Client v{0} on {1} / {2}",
            //    ApiConfig.Version,
            //    Environment.Version,
            //    Environment.OSVersion);

            request.Accept = "application/json";

            AddHeaders(request, env);
            AddCustomHeaders(request, headers);

            //request.Timeout = env.ConnectTimeout;
            //request.ReadWriteTimeout = env.ReadTimeout;

            return request;
        }

        private static void AddHeaders(HttpWebRequest request, ApiConfig env)
        {
            request.Headers[HttpRequestHeader.AcceptCharset] = env.Charset;
            request.Headers[HttpRequestHeader.Authorization] = env.AuthValue;
        }

        private static void AddCustomHeaders(HttpWebRequest request, Dictionary<string, string> headers)
        {
            foreach (KeyValuePair<string, string> entry in headers)
            {
                AddHeader(request, entry.Key, entry.Value);
            }
        }

        private static void AddHeader(HttpWebRequest request, String headerName, String value)
        {
            request.Headers[headerName] = value;
        }

        private static async Task<(string content, HttpStatusCode code)> SendRequest(HttpWebRequest request)
        {
            try
            {
                using (HttpWebResponse response = request.GetResponseAsync().Result as HttpWebResponse)
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    var code = response.StatusCode;
                    var content = await reader.ReadToEndAsync();
                    return (content, code);
                }
            }
            catch (WebException ex)
            {
                if (ex.Response == null) throw ex;
                using (HttpWebResponse response = ex.Response as HttpWebResponse)
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    var code = response.StatusCode;
                    string content = await reader.ReadToEndAsync();
                    Dictionary<string, string> errorJson = null;
                    try
                    {
                        errorJson = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                    }
                    catch (JsonException e)
                    {
                        throw new ArgumentException("Not in JSON format. Probably not a ChargeBee response. \n " + content, e);
                    }
                    string type = "";
                    errorJson.TryGetValue("type", out type);
                    if ("payment".Equals(type))
                    {
                        throw new PaymentException(response.StatusCode, errorJson);
                    }
                    else if ("operation_failed".Equals(type))
                    {
                        throw new OperationFailedException(response.StatusCode, errorJson);
                    }
                    else if ("invalid_request".Equals(type))
                    {
                        throw new InvalidRequestException(response.StatusCode, errorJson);
                    }
                    else
                    {
                        throw new ApiException(response.StatusCode, errorJson);
                    }
                }
            }
        }

        private static Task<(string content, HttpStatusCode code)> GetJson(string url, Params parameters, ApiConfig env, Dictionary<string, string> headers, bool IsList)
        {
            url = String.Format("{0}?{1}", url, parameters.GetQuery(IsList));
            HttpWebRequest request = GetRequest(url, HttpMethod.GET, headers, env);
            return SendRequest(request);
        }

        public static async Task<EntityResult> Post(string url, Params parameters, Dictionary<string, string> headers, ApiConfig env)
        {
            HttpWebRequest request = GetRequest(url, HttpMethod.POST, headers, env);
            byte[] paramsBytes =
                Encoding.GetEncoding(env.Charset).GetBytes(parameters.GetQuery(false));

            //request.ContentLength = paramsBytes.Length;
            request.ContentType =
                String.Format("application/x-www-form-urlencoded;charset={0}", env.Charset);
            using (Stream stream = request.GetRequestStreamAsync().Result)
            {
                stream.Write(paramsBytes, 0, paramsBytes.Length);

                (string json, HttpStatusCode code) = await SendRequest(request);

                EntityResult result = new EntityResult(code, json);
                return result;
            }
        }

        public static async Task<EntityResult> Get(string url, Params parameters, Dictionary<string, string> headers, ApiConfig env)
        {
            (string json, HttpStatusCode code) = await GetJson(url, parameters, env, headers, false);

            EntityResult result = new EntityResult(code, json);
            return result;
        }

        public static async Task<ListResult> GetList(string url, Params parameters, Dictionary<string, string> headers, ApiConfig env)
        {
            (string json, HttpStatusCode code) = await GetJson(url, parameters, env, headers, true);

            ListResult result = new ListResult(code, json);
            return result;
        }

        public static DateTime ConvertFromTimestamp(long timestamp)
        {
            return m_unixTime.AddSeconds(timestamp).ToLocalTime();
        }

        public static long? ConvertToTimestamp(DateTime? t)
        {
            if (t == null) return null;

            DateTime dtutc = ((DateTime)t).ToUniversalTime();

            if (dtutc < m_unixTime) throw new ArgumentException("Time can't be before 1970, January 1!");

            return (long)(dtutc - m_unixTime).TotalSeconds;
        }
    }

    /// <summary>
    /// HTTP method
    /// </summary>
    public enum HttpMethod
    {
        /// <summary>
        /// DELETE
        /// </summary>
        DELETE,
        /// <summary>
        /// GET
        /// </summary>
        GET,
        /// <summary>
        /// POST
        /// </summary>
        POST,
        /// <summary>
        /// PUT
        /// </summary>
        PUT
    }
}
