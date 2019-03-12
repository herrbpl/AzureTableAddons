using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AzureTableAddons
{


    internal class ConfigEntity : TableEntity
    {        
        public ConfigEntity(string partitionkey, string deviceId)
        {
            this.PartitionKey = partitionkey;
            this.RowKey = deviceId;
        }

        public ConfigEntity() { }

        public string Data { get; set; }

    }

    public class ConfigurationOptions
    {
        public string ConnectionString { get; set; }
        public string TableName { get; set; } = "Configuration";
        public string Environment { get; set; } = "Production";
    }

    internal class Configuration
    {
        private readonly ConfigurationOptions _configurationOptions;
        

        private CloudTableClient _tableClient;
        private CloudTable _table;

        public Configuration(ConfigurationOptions configurationOptions)
        {
            _configurationOptions = configurationOptions;
        }

        private async Task Initialize()
        {
            if (_tableClient == null)
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_configurationOptions.ConnectionString);

                // create client
                _tableClient = storageAccount.CreateCloudTableClient();

                // Create the CloudTable object that represents the table.
                _table = _tableClient.GetTableReference(_configurationOptions.TableName);

                // Create the table if it doesn't exist.
                await _table.CreateIfNotExistsAsync();

            }
        }

        public async Task<string> GetConfigurationAsync(string configurationKey)
        {
            
            await Initialize();
            
            // check if exist. If not, create new configuration
            TableOperation retrieveOperation = TableOperation.Retrieve<ConfigEntity>(_configurationOptions.Environment, configurationKey);

            TableResult retrievedResult = await _table.ExecuteAsync(retrieveOperation);




            string configstr = "";
            if (retrievedResult.Result == null)
            {
                return null;                
            }
            else
            {
                configstr = ((ConfigEntity)retrievedResult.Result).Data;

            }

            return configstr;
        }


    }


}
