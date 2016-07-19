using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class GlobalStateUtility
    {
        private static string GetInitializationCode(string initializations)
        {
            //All the neccesary wrapper code to get the initial values of the variables. 
            //todo: probably a less awkward way to obtain these values
            string code = @"
                using System.Collections.Generic;
                using System.Collections.ObjectModel;
                using System.Reflection;
                public static class State {{
                    {0}
                public static Collection<KeyValuePair<string, object>> GetState() {{ 
                    Collection<KeyValuePair<string,object>> variableLIst = new Collection<KeyValuePair<string,object>>();
                    foreach (FieldInfo field in typeof(State).GetFields()) {{
                        variableLIst.Add(new KeyValuePair<string, object>(field.Name, field.GetValue(null)));
                    }}
                    return variableLIst;
                 }}
            }}";
            return string.Format(code, initializations);
        }

        private static Assembly GetInitialStateAssembly(string initializers)
        {
            //generate an assembly using Roslyn
            using (var stream = new MemoryStream())
            {
                string assemblyFileName = "gen" + Guid.NewGuid().ToString().Replace("-", "") + ".dll";

                CSharpCompilation compilation = CSharpCompilation.Create(assemblyFileName,
                    new[] { CSharpSyntaxTree.ParseText(GetInitializationCode(initializers)) },
                    new[]
                    {
                        MetadataReference.CreateFromFile(typeof (object).Assembly.Location)
                    },
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    );
                compilation.Emit(stream);
                Assembly assembly = Assembly.Load(stream.GetBuffer());
                return assembly;
            }
        }

        public static void ProcessInitialState(ApiConfig config)
        {
            //Generate the assembly for code to get all the variables and their initial values
            Assembly assembly = GetInitialStateAssembly(config.GlobalState);
            Type stateType = assembly.GetType("State");
            MethodInfo getStateMethod = stateType.GetMethod("GetState");
            var stateList =
                (Collection<KeyValuePair<string, object>>)
                    getStateMethod.Invoke(null, BindingFlags.InvokeMethod, null, null, CultureInfo.CurrentCulture);

            //send the initial values to the database
            UpdateOrCreateState(config, stateList);
        }

        public static void UpdateOrCreateState(ApiConfig config, Collection<KeyValuePair<string, object>> variableList)
        {
            //Create a table object given the table storage information.
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(config.TableStorage.Table);

            TableOperation operation;
            foreach (var variable in variableList)
            {
                //checks if the variable is a "scalar" type, and if so, creates a ScalarEntity<T> and inserts it into Azure Storage
                Type type = variable.Value.GetType();
                if (type.IsPrimitive || type == typeof(Decimal) || type == typeof(string))
                {
                    var scalar = new DynamicTableEntity(config.TableStorage.PartitionKey, variable.Key);
                    dynamic value = Convert.ChangeType(variable.Value, type);
                    scalar["Value"] = new EntityProperty(value);
                    operation = TableOperation.InsertOrReplace(scalar);
                    table.Execute(operation);
                }
            }
        }

        public static IDictionary<string, object> RetrieveState(TableDetails tableInfo, Collection<string> variables)
        {
            IDictionary<string, object> variableStates = new Dictionary<string, object>();
            if (tableInfo == null || variables == null)
            {
                return variableStates;
            }

            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(tableInfo.Table);

            foreach(var variable in variables)
            {
                TableOperation query =
                    TableOperation.Retrieve<DynamicTableEntity>(tableInfo.PartitionKey + "_" + variable,
                        variable);
                DynamicTableEntity result = table.Execute(query).Result as DynamicTableEntity;
                if (result != null)
                {
                    variableStates.Add(variable, result["Value"].PropertyAsObject);
                }
            }
            return variableStates;
        } 
    }
}
