/*
 * OpenAPI Petstore
 *
 * This spec is mainly for testing Petstore server and contains fake endpoints, models. Please do not use this for any other purpose. Special characters: \" \\
 *
 * The version of the OpenAPI document: 1.0.0
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;
using System.Net.Http;
using Polly;

namespace Org.OpenAPITools.Client
{
    /// <summary>
    /// To Serialize/Deserialize JSON using our custom logic, but only when ContentType is JSON.
    /// </summary>
    internal class CustomJsonCodec
    {
        private readonly IReadableConfiguration _configuration;
        private static readonly string _contentType = "application/json";
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            // OpenAPI generated types generally hide default constructors.
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            }
        };

        public CustomJsonCodec(IReadableConfiguration configuration)
        {
            _configuration = configuration;
        }

        public CustomJsonCodec(JsonSerializerSettings serializerSettings, IReadableConfiguration configuration)
        {
            _serializerSettings = serializerSettings;
            _configuration = configuration;
        }

        /// <summary>
        /// Serialize the object into a JSON string.
        /// </summary>
        /// <param name="obj">Object to be serialized.</param>
        /// <returns>A JSON string.</returns>
        public string Serialize(object obj)
        {
            if (obj != null && obj is Org.OpenAPITools.Model.AbstractOpenAPISchema)
            {
                // the object to be serialized is an oneOf/anyOf schema
                return ((Org.OpenAPITools.Model.AbstractOpenAPISchema)obj).ToJson();
            }
            else
            {
                return JsonConvert.SerializeObject(obj, _serializerSettings);
            }
        }

        public T Deserialize<T>(HttpResponseMessage response)
        {
            var result = (T)Deserialize(response, typeof(T));
            return result;
        }

        /// <summary>
        /// Deserialize the JSON string into a proper object.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <param name="type">Object type.</param>
        /// <returns>Object representation of the JSON string.</returns>
        internal object Deserialize(HttpResponseMessage response, Type type)
        {
            IList<string> headers = response.Headers.Select(x => x.Key + "=" + x.Value).ToList();

            if (type == typeof(byte[])) // return byte array
            {
                return response.Content.ReadAsByteArrayAsync().Result;
            }

            // TODO: ? if (type.IsAssignableFrom(typeof(Stream)))
            if (type == typeof(Stream))
            {
                var bytes = response.Content.ReadAsByteArrayAsync().Result;
                if (headers != null)
                {
                    var filePath = String.IsNullOrEmpty(_configuration.TempFolderPath)
                        ? Path.GetTempPath()
                        : _configuration.TempFolderPath;
                    var regex = new Regex(@"Content-Disposition=.*filename=['""]?([^'""\s]+)['""]?$");
                    foreach (var header in headers)
                    {
                        var match = regex.Match(header.ToString());
                        if (match.Success)
                        {
                            string fileName = filePath + ClientUtils.SanitizeFilename(match.Groups[1].Value.Replace("\"", "").Replace("'", ""));
                            File.WriteAllBytes(fileName, bytes);
                            return new FileStream(fileName, FileMode.Open);
                        }
                    }
                }
                var stream = new MemoryStream(bytes);
                return stream;
            }

            if (type.Name.StartsWith("System.Nullable`1[[System.DateTime")) // return a datetime object
            {
                return DateTime.Parse(response.Content.ReadAsStringAsync().Result, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            if (type == typeof(String) || type.Name.StartsWith("System.Nullable")) // return primitive type
            {
                return Convert.ChangeType(response.Content.ReadAsStringAsync().Result, type);
            }

            // at this point, it must be a model (json)
            try
            {
                return JsonConvert.DeserializeObject(response.Content.ReadAsStringAsync().Result, type, _serializerSettings);
            }
            catch (Exception e)
            {
                throw new ApiException(500, e.Message);
            }
        }

        public string RootElement { get; set; }
        public string Namespace { get; set; }
        public string DateFormat { get; set; }

        public string ContentType
        {
            get { return _contentType; }
            set { throw new InvalidOperationException("Not allowed to set content type."); }
        }
    }
    /// <summary>
    /// Provides a default implementation of an Api client (both synchronous and asynchronous implementatios),
    /// encapsulating general REST accessor use cases.
    /// </summary>
    public partial class ApiClient : ISynchronousClient, IAsynchronousClient
    {
        private readonly String _baseUrl;

        /// <summary>
        /// Specifies the settings on a <see cref="JsonSerializer" /> object.
        /// These settings can be adjusted to accomodate custom serialization rules.
        /// </summary>
        public JsonSerializerSettings SerializerSettings { get; set; } = new JsonSerializerSettings
        {
            // OpenAPI generated types generally hide default constructors.
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiClient" />, defaulting to the global configurations' base url.
        /// </summary>
        public ApiClient()
        {
            _baseUrl = Org.OpenAPITools.Client.GlobalConfiguration.Instance.BasePath;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiClient" />
        /// </summary>
        /// <param name="basePath">The target service's base path in URL format.</param>
        /// <exception cref="ArgumentException"></exception>
        public ApiClient(String basePath)
        {
            if (string.IsNullOrEmpty(basePath))
                throw new ArgumentException("basePath cannot be empty");

            _baseUrl = basePath;
        }

        /// <summary>
        /// Provides all logic for constructing a new HttpRequestMessage.
        /// At this point, all information for querying the service is known. Here, it is simply
        /// mapped into the a HttpRequestMessage.
        /// </summary>
        /// <param name="method">The http verb.</param>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>[private] A new HttpRequestMessage instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private HttpRequestMessage NewRequest(
            HttpMethod method,
            String path,
            RequestOptions options,
            IReadableConfiguration configuration)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (options == null) throw new ArgumentNullException("options");
            if (configuration == null) throw new ArgumentNullException("configuration");

            WebRequestPathBuilder builder = new WebRequestPathBuilder(_baseUrl, path);

            builder.AddPathParameters(options.PathParameters);

            // In case of POST or PUT pass query parameters in request body
            if (method != HttpMethod.Post && method != HttpMethod.Put)
            {
                builder.AddQueryParameters(options.QueryParameters);
            }

            HttpRequestMessage request = new HttpRequestMessage(method, builder.GetFullUri());

            if (configuration.DefaultHeaders != null)
            {
                foreach (var headerParam in configuration.DefaultHeaders)
                {
                    request.Headers.Add(headerParam.Key, headerParam.Value);
                }
            }

            if (options.HeaderParameters != null)
            {
                foreach (var headerParam in options.HeaderParameters)
                {
                    foreach (var value in headerParam.Value)
                    {
                        // Todo make content headers actually content headers
                        request.Headers.TryAddWithoutValidation(headerParam.Key, value);
                    }
                }
            }

            List<Tuple<HttpContent, string, string>> contentList = new List<Tuple<HttpContent, string, string>>();

            if (options.FormParameters != null && options.FormParameters.Count > 0)
            {
                contentList.Add(new Tuple<HttpContent, string, string>(new FormUrlEncodedContent(options.FormParameters), null, null));
            }

            if (options.Data != null)
            {
                var serializer = new CustomJsonCodec(SerializerSettings, configuration);
                contentList.Add(
                    new Tuple<HttpContent, string, string>(new StringContent(serializer.Serialize(options.Data), new UTF8Encoding(), "application/json"), null, null));
            }

            if (options.FileParameters != null && options.FileParameters.Count > 0)
            {
                foreach (var fileParam in options.FileParameters)
                {
                    var bytes = ClientUtils.ReadAsBytes(fileParam.Value);
                    var fileStream = fileParam.Value as FileStream;
                    contentList.Add(new Tuple<HttpContent, string, string>(new ByteArrayContent(bytes), fileParam.Key,
                        fileStream?.Name ?? "no_file_name_provided"));
                }
            }

            if (contentList.Count > 1)
            {
                string boundary = "---------" + Guid.NewGuid().ToString().ToUpperInvariant();
                var multipartContent = new MultipartFormDataContent(boundary);
                foreach (var content in contentList)
                {
                   if(content.Item2 != null)
                   {
                     multipartContent.Add(content.Item1, content.Item2, content.Item3);
                   }
                   else
                   {
                     multipartContent.Add(content.Item1);
                   }
                }

                request.Content = multipartContent;
            }
            else
            {
                request.Content = contentList.FirstOrDefault()?.Item1;
            }

            // TODO provide an alternative that allows cookies per request instead of per API client
            if (options.Cookies != null && options.Cookies.Count > 0)
            {
                request.Properties["CookieContainer"] = options.Cookies;
            }

            return request;
        }

        partial void InterceptRequest(HttpRequestMessage req, HttpClientHandler handler);
        partial void InterceptResponse(HttpRequestMessage req, HttpResponseMessage response);

        private ApiResponse<T> ToApiResponse<T>(HttpResponseMessage response, object responseData, HttpClientHandler handler, Uri uri)
        {
            T result = (T) responseData;
            string rawContent = response.Content.ToString();

            var transformed = new ApiResponse<T>(response.StatusCode, new Multimap<string, string>(), result, rawContent)
            {
                ErrorText = response.ReasonPhrase,
                Cookies = new List<Cookie>()
            };

            if (response.Headers != null)
            {
                foreach (var responseHeader in response.Headers)
                {

                }
            }

            if (response != null)
            {
                foreach (Cookie cookie in handler.CookieContainer.GetCookies(uri))
                {
                   transformed.Cookies.Add(cookie);
                }
            }

            return transformed;
        }

        private ApiResponse<T> Exec<T>(HttpRequestMessage req, IReadableConfiguration configuration)
        {
            return ExecAsync<T>(req, configuration).Result;
        }

        private async Task<ApiResponse<T>> ExecAsync<T>(HttpRequestMessage req,
            IReadableConfiguration configuration,
            System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var handler = new HttpClientHandler();
            var client = new HttpClient();
            var deserializer = new CustomJsonCodec(SerializerSettings, configuration);

            var finalToken = cancellationToken;

            if (configuration.Timeout > 0)
            {
                var tokenSource = new CancellationTokenSource(configuration.Timeout);
                finalToken = CancellationTokenSource.CreateLinkedTokenSource(finalToken, tokenSource.Token).Token;
            }

            if (configuration.Proxy != null)
            {
                handler.Proxy = configuration.Proxy;
            }

            if (configuration.UserAgent != null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", configuration.UserAgent);
            }

            if (configuration.ClientCertificates != null)
            {
                handler.ClientCertificates.AddRange(configuration.ClientCertificates);
            }

            var cookieContainer = req.Properties.ContainsKey("CookieContainer") ? req.Properties["CookieContainer"] as List<Cookie> : null;

            if (cookieContainer != null)
            {
                foreach (var cookie in cookieContainer)
                {
                    handler.CookieContainer.Add(cookie);
                }
            }

            InterceptRequest(req, handler);

            HttpResponseMessage response;
            if (RetryConfiguration.AsyncRetryPolicy != null)
            {
                var policy = RetryConfiguration.AsyncRetryPolicy;
                var policyResult = await policy
                    .ExecuteAndCaptureAsync(() => client.SendAsync(req, cancellationToken))
                    .ConfigureAwait(false);
                response = (policyResult.Outcome == OutcomeType.Successful) ?
                    policyResult.Result : new HttpResponseMessage()
                    {
                        ReasonPhrase = policyResult.FinalException.ToString(),
                        RequestMessage = req
                    };
            }
            else
            {
                response = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
            }

            object responseData = deserializer.Deserialize<T>(response);

            // if the response type is oneOf/anyOf, call FromJSON to deserialize the data
            if (typeof(Org.OpenAPITools.Model.AbstractOpenAPISchema).IsAssignableFrom(typeof(T)))
            {
                T instance = (T) Activator.CreateInstance(typeof(T));
                MethodInfo method = typeof(T).GetMethod("FromJson");
                method.Invoke(instance, new object[] {response.Content});
                responseData = instance;
            }
            else if (typeof(T).Name == "Stream") // for binary response
            {
                responseData = (T) (object) await response.Content.ReadAsStreamAsync();
            }

            InterceptResponse(req, response);

            var result = ToApiResponse<T>(response, responseData, handler, req.RequestUri);

            return result;
        }

        #region IAsynchronousClient
        /// <summary>
        /// Make a HTTP GET request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> GetAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Get, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP POST request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> PostAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Post, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP PUT request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> PutAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Put, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP DELETE request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> DeleteAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Delete, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP HEAD request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> HeadAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Head, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP OPTION request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> OptionsAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(HttpMethod.Options, path, options, config), config, cancellationToken);
        }

        /// <summary>
        /// Make a HTTP PATCH request (async).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <param name="cancellationToken">Token that enables callers to cancel the request.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public Task<ApiResponse<T>> PatchAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(new HttpMethod("PATCH"), path, options, config), config, cancellationToken);
        }
        #endregion IAsynchronousClient

        #region ISynchronousClient
        /// <summary>
        /// Make a HTTP GET request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Get<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Get, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP POST request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Post<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Post, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP PUT request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Put<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Put, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP DELETE request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Delete<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Delete, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP HEAD request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Head<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Head, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP OPTION request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Options<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(HttpMethod.Options, path, options, config), config);
        }

        /// <summary>
        /// Make a HTTP PATCH request (synchronous).
        /// </summary>
        /// <param name="path">The target path (or resource).</param>
        /// <param name="options">The additional request options.</param>
        /// <param name="configuration">A per-request configuration object. It is assumed that any merge with
        /// GlobalConfiguration has been done before calling this method.</param>
        /// <returns>A Task containing ApiResponse</returns>
        public ApiResponse<T> Patch<T>(string path, RequestOptions options, IReadableConfiguration configuration = null)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(new HttpMethod("PATCH"), path, options, config), config);
        }
        #endregion ISynchronousClient
    }
}
