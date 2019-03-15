using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace AzureTableAddons
{
    
    public class AzureTableColumn
    {
        [JsonProperty(PropertyName = "default")]
        public dynamic Default { get; set; } = null;
        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; } = null;
    }

    public class AzureTableSchema
    {

        [JsonProperty(PropertyName = "sourceurl")]
        public string SourceUrl { get; set; } = "";
        [JsonProperty(PropertyName = "removeunspecified")]
        public bool RemoveUnspecified { get; set; } = false;
        [JsonProperty(PropertyName = "addmissing")]
        public bool AddMissing { get; set; } = true;
        [JsonProperty(PropertyName = "columns")]
        public IDictionary<string, AzureTableColumn> Columns = new Dictionary<string,AzureTableColumn>();
    }
}
