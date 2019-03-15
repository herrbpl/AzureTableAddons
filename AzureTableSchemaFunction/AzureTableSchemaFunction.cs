using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net.Http.Headers;
using System.Collections.Generic;
using Microsoft.AspNetCore.WebUtilities;

namespace AzureTableAddons
{
    // config storage. Use 
    
    public static class AzureTableSchemaFunction
    {
        private static HttpClient httpClient = new HttpClient();        

        [FunctionName("AzureTableSchemaFunction")]        
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{datasetname}")] HttpRequest req,
            string datasetname,
            ILogger log, ExecutionContext context)
        {            
            
            var baseconfig = new ConfigurationBuilder()           
           .SetBasePath(context.FunctionAppDirectory)
           .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables()           
           .Build();

            var configOptions = new ConfigurationOptions();
            baseconfig.Bind("AzureTableSchema", configOptions);
            if (configOptions.Environment == null || configOptions.Environment == "")
            {
                configOptions.Environment = baseconfig["ASPNETCORE_ENVIRONMENT"];
            }

            if (configOptions.ConnectionString == null || configOptions.ConnectionString == "")
            {
                log.LogError($"ConnectionString is not specified");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'service misconfiguration' }")
                };
            }

            Configuration configuration = new Configuration(configOptions);

            string datasetconfigStr = null;
            try
            {
                datasetconfigStr = await configuration.GetConfigurationAsync(datasetname);
            } catch( Exception e)
            {
                log.LogError(e, $"Unable to query config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'dataset not found' }")
                };
            }

            if (datasetconfigStr == null)
            {
                log.LogError($"Unable to find config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{ 'error': 'dataset not found' }")
                };
            }

            AzureTableSchema schema = null;

            try
            {
                schema = JsonConvert.DeserializeObject<AzureTableSchema>(datasetconfigStr, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include });                

            } catch (Exception e)
            {
                log.LogError(e, $"Unable to deserialize config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'service misconfiguration' }")
                };
            }



            // starting with result.


            var result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new PushStreamContent(async (outputStream, httpContext, transportContext) =>
                {

                    // Add query parameters passby
                    // https://benjii.me/2017/04/parse-modify-query-strings-asp-net-core/

                    var uri = new Uri(schema.SourceUrl);
                    var baseUri = uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);

                    var query = QueryHelpers.ParseQuery(uri.Query);

                    var qb = new QueryBuilder();
                    foreach (var item in query)
                    {
                        qb.Add(item.Key, item.Value.AsEnumerable());
                    }

                    

                    List<string> allowedParameters = new List<string>()
                    {
                        "$filter", "$orderby", "$select", "$skip", "$top"
                    };
                    
                    foreach (var item in req.Query)
                    {
                        if (allowedParameters.Contains(item.Key) && !query.ContainsKey(item.Key)) // do not allow to overwrite existing values
                        {
                            qb.Add(item.Key, item.Value.AsEnumerable());
                        }
                    }

                    var targetUrl = baseUri + qb.ToQueryString();

                    
                    //HttpRequestMessage r = new HttpRequestMessage(HttpMethod.Get, schema.SourceUrl);
                    HttpRequestMessage r = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    r.Headers.Add("Accept", "application / json; odata = nometadata");

                    
                    // TODO: Add request query string parameters to request


                    var response = await httpClient.SendAsync(r);
                    var serializer = new JsonSerializer();
                    //var ss = new StringBuilder();
                    bool inValueArray = false;
                    string lastPropertyName = "";


                    using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync(), new UTF8Encoding(), false))
                    using (var jsonTextReader = new JsonTextReader(sr))
                    using (JsonWriter writer = new JsonTextWriter(new StreamWriter(outputStream, new UTF8Encoding(), 512, false)))
                    {
                     

                        while (jsonTextReader.Read())
                        {
                            if (!inValueArray)
                            {                                                                
                                if (jsonTextReader.TokenType == JsonToken.PropertyName) { lastPropertyName = jsonTextReader.Value.ToString(); }
                                else if (jsonTextReader.TokenType == JsonToken.StartArray && lastPropertyName == "value")
                                {
                                    inValueArray = true;
                                    // write object beginning and such..
                                    await writer.WriteStartObjectAsync();
                                    await writer.WritePropertyNameAsync("value");
                                    await writer.WriteStartArrayAsync();
                                    
                                }

                            }
                            else
                            {
                                if (jsonTextReader.TokenType == JsonToken.StartObject) // read entire object
                                {
                                    JObject obj = JObject.Load(jsonTextReader);

                                    // add missing columns
                                    if (schema.AddMissing)
                                    {
                                        foreach (var item in schema.Columns)
                                        {
                                            if (!obj.ContainsKey(item.Key))
                                            {
                                                obj.Add(item.Key, item.Value.Default);
                                            }
                                        }
                                    }

                                    // remove unspecified columns
                                    if (schema.RemoveUnspecified)
                                    {
                                        var props = obj.Properties().Select(a => a.Name).ToList();

                                        foreach (var item in props)
                                        {
                                            if (!schema.Columns.ContainsKey(item))
                                            {
                                                obj.Remove(item);
                                            }
                                        }
                                    }
                                                                     
                                    var objstr = JsonConvert.SerializeObject(obj,
                                            new JsonSerializerSettings()
                                            {
                                                NullValueHandling = NullValueHandling.Include,
                                                Formatting = Formatting.None
                                            }
                                    );

                                    log.LogInformation(objstr);

                                    await writer.WriteRawValueAsync(objstr);
                                    await writer.FlushAsync();
                                    
                                }
                                else if (jsonTextReader.TokenType == JsonToken.EndArray)
                                {
                                    await writer.WriteEndArrayAsync();
                                    await writer.WriteEndObjectAsync();        
                                    
                                    inValueArray = false;

                                }

                            }
                        }
                    }
                                        
                    outputStream.Close();                   
                }),
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"{datasetname}.json" };
            
            return result;


            
        }


        [FunctionName("AzureTableSchemaFunctionSchema")]
        public static async Task<HttpResponseMessage> RunSchema(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{datasetname}/schema")] HttpRequest req,
            string datasetname,
            ILogger log, ExecutionContext context)
        {

            var baseconfig = new ConfigurationBuilder()
           .SetBasePath(context.FunctionAppDirectory)
           .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables()
           .Build();

            var configOptions = new ConfigurationOptions();
            baseconfig.Bind("AzureTableSchema", configOptions);
            if (configOptions.Environment == null || configOptions.Environment == "")
            {
                configOptions.Environment = baseconfig["ASPNETCORE_ENVIRONMENT"];
            }

            if (configOptions.ConnectionString == null || configOptions.ConnectionString == "")
            {
                log.LogError($"ConnectionString is not specified");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'service misconfiguration' }")
                };
            }

            Configuration configuration = new Configuration(configOptions);

            string datasetconfigStr = null;
            try
            {
                datasetconfigStr = await configuration.GetConfigurationAsync(datasetname);
            }
            catch (Exception e)
            {
                log.LogError(e, $"Unable to query config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'dataset not found' }")
                };
            }

            if (datasetconfigStr == null)
            {
                log.LogError($"Unable to find config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{ 'error': 'dataset not found' }")
                };
            }

            AzureTableSchema schema = null;

            try
            {
                schema = JsonConvert.DeserializeObject<AzureTableSchema>(datasetconfigStr, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include });

            }
            catch (Exception e)
            {
                log.LogError(e, $"Unable to deserialize config string with key '{datasetname}'");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{ 'error': 'service misconfiguration' }")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(schema.Columns, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include }))
            };
            




        }
    }
}
