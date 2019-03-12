using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Net;
using Newtonsoft.Json.Linq;
using SFA.DAS.Configuration;
using SFA.DAS.Configuration.AzureTableStorage;

namespace Test
{
    // config storage. Use 
    
    public static class AzureTableSchemaFunction
    {
        private static HttpClient httpClient = new HttpClient();        

        [FunctionName("AzureTableSchemaFunction")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "{datasetname}")] HttpRequest req,
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
            

            // https://forums.asp.net/t/1998451.aspx?Deserialize+Json+String+in+Net+without+creating+Type+or+anonymous+type



            // query
            //string url = "https://mntiotroadweather4data.table.core.windows.net/MeasurementsRoadsensorsLatest?sv=2018-03-28&si=public&tn=measurementsroadsensorslatest&sig=47K41Vp9bwj8O1PFQYEAYgZdygoLP8oaiSJfO0UtClA%3D";
            HttpRequestMessage r = new HttpRequestMessage(HttpMethod.Get, schema.SourceUrl);
            
            r.Headers.Add("Accept", "application / json; odata = nometadata");

            var response = await httpClient.SendAsync(r);



            // http://www.drdobbs.com/windows/parsing-big-records-with-jsonnet/240165316
            // https://github.com/JamesNK/Newtonsoft.Json/issues/645
            // https://blog.stephencleary.com/2016/10/async-pushstreamcontent.html
            // https://www.newtonsoft.com/json/help/html/CustomJsonReader.htm

            // custom hack to read odata json output, with no metadata
            var serializer = new JsonSerializer();
            var ss = new StringBuilder();
            bool inValueArray = false;
            string lastPropertyName = "";
            using (var sr = new StreamReader(await response.Content.ReadAsStreamAsync(), new UTF8Encoding(), false))
            using (var jsonTextReader = new JsonTextReader(sr))
            {
               // var jtree = serializer.Deserialize(jsonTextReader);
               // var result = jtree.ToString();

                while (jsonTextReader.Read())
                {
                    if (!inValueArray)
                    {
                        if (jsonTextReader.TokenType == JsonToken.PropertyName ) { lastPropertyName = jsonTextReader.Value.ToString();  }
                        else if (jsonTextReader.TokenType == JsonToken.StartArray && lastPropertyName == "value") { inValueArray = true;  }
                        

                    } else
                    {                        
                        if (jsonTextReader.TokenType == JsonToken.StartObject) // read entire object
                        {
                            JObject obj = JObject.Load(jsonTextReader);
                            log.LogInformation(obj["deviceId"] + " - " + obj["description"]);
                            if (!obj.ContainsKey("mymissingproperty")) {
                                obj.Add("mymissingproperty", null);
                            }
                            /*
                            foreach (var item in obj.Properties())
                            {
                                ss.AppendLine($"Property: {item.Name}");
                            }
                            */

                            var objstr = JsonConvert.SerializeObject(obj, 
                                    new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Include, Formatting = Formatting.Indented
                                    }
                            );

                            ss.AppendLine(objstr);
                        } else if (jsonTextReader.TokenType == JsonToken.EndArray) { inValueArray = false; }
                        
                    }
                }

                //ss.Append(result);
                //log.LogInformation(result);

            }

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ss.ToString())
            };

           



                log.LogInformation("C# HTTP trigger function processed a request.");
                
                return resp;
            
        }
    }
}
