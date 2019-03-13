# AzureTableAddons
Add schema imitation to Azure Table OData request.

We had a customer case where data stored in Azure Table needed to have some kind of schema, 
so consuming party would know, which fields should exist in OData query from Azure Table. Also, they needed 
non-sparse result for simplicity, meaning that if field value in result is null, field name is still 
written to result instead of omitting it.

As stopgap solution, instead of changing backend data storage, we decided to put some kind of scheming :) proxy
in front of Azure Table. For simplicity's sake, HTTP-triggered Azure Function is used.

Azure function is called as follows:

* https://your-function-app.azurewebsites.net/api/{dataset} - gives back proxied result which is configured with *dataset* configurationkey in configuration
* https://your-function-app.azurewebsites.net/api/{dataset}/schema - gives back schema for *dataset*


## Configuration

Configuration resides in Azure Table as well. On Azure Function side, following Application settings need to be set:

* **AzureTableSchema__ConnectionString** - connection string for Azure Storage Account containing configuration.
* **AzureTableSchema__TableName** - name of configuration table.
* **AzureTableSchema__Environment** - Environment, used as partition key of Azure Table.

**AzureTableSchema__TableName** Rowkey is used configuration key for looking up configuration. It also consists field Data which holds 
AzureTableSchema configuration in JSON format.

## AzureTableSchema

AzureTableSchema is JSON structure, which tells where is original data source, which fields dataset should exist in result and optionally,
which are default values and types for fields.

No authentication is currently supported for source urls besides SAS signature. Column definition can be left as empty object if no defaults are required. 
Type information is currently just for information, no validation/conversion is done by proxy.

Example of Data field definition in Azure configuration Table:
```json
{
  "sourceurl": "your azure table source url with SAS signature", 
  "columns": {
    "deviceid": { 
      "default": "defaultid", 
      "type": "string" 
    },
    "timeStamp": {},
    "eventdatetime": {},
    "devicemodel": {},
    "description": {},
  },
  "removeunspecified": "false"
}
```
