using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script
{

    public static class GlobalStateUtility
    {
        private static Regex variableNameSplitRegex = new Regex("(?<!|)_");
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
            IDictionary<string, object> initialState = new Dictionary<string, object>();
            foreach (var pair in stateList)
            {
                initialState.Add(pair);
            }
            UpdateOrCreateState(config, initialState);
        }

        public static string TranslateKey(string currentKey, string newKey)
        {
            if (string.IsNullOrEmpty(currentKey))
            {
                return newKey;
            }
            return currentKey + "_" + newKey;
        }

        public static string EscapeAndTranslateKey(string currentKey, string newKey)
        {
            if (string.IsNullOrEmpty(currentKey))
            {
                return EscapeKey(newKey);
            }
            return currentKey + "_" + EscapeKey(newKey);
        }

        public static string EscapeKey(string key)
        {
            return key.Replace("|", "||").Replace("_", "|_");
        }

        public static string UnescapeKey(string key)
        {
            return key.Replace("||", "|").Replace("|_", "_");
        }

        private static void TraverseVariables(ApiConfig config, string currentKey, CloudTable table, IDictionary<string, object> variables)
        {
            TableOperation operation;
            string partitionKey = string.IsNullOrEmpty(currentKey)
                ? EscapeKey(config.TableStorage.PartitionKey) + "_labels"
                : EscapeKey(config.TableStorage.PartitionKey);
            foreach (var variable in variables)
            {
                //checks if the variable is a "scalar" type, and if so, creates a ScalarEntity<T> and inserts it into Azure Storage
                Type type = variable.Value.GetType();
                if (type.IsPrimitive || type == typeof(decimal) || type == typeof(string))
                {
                    var scalar = new DynamicTableEntity(partitionKey, EscapeAndTranslateKey(currentKey, variable.Key));
                    dynamic value = Convert.ChangeType(variable.Value, type);
                    scalar["Value"] = new EntityProperty(value);
                    scalar["type"] = new EntityProperty(type.ToString()); 
                    operation = TableOperation.InsertOrReplace(scalar);
                    table.Execute(operation);
                }
                else if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    IDictionary<string, object> dict =
                        (variable.Value as IDictionary).Cast<dynamic>().ToDictionary(entry => (string) entry.Key, entry => entry.Value);
                    string newKey = TranslateKey(currentKey, variable.Key);
                    var dictLabel = new DynamicTableEntity(partitionKey,
                        EscapeAndTranslateKey(currentKey, variable.Key));
                    dictLabel["type"] = new EntityProperty(variable.Value.GetType().ToString());
                    operation = TableOperation.InsertOrReplace(dictLabel);
                    table.Execute(operation);
                    TraverseVariables(config, newKey, table, dict);
                }
            }
        }

        public static void UpdateOrCreateState(ApiConfig config, IDictionary<string, object> variables)
        {
            //Create a table object given the table storage information.
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(config.TableStorage.Table);

            TraverseVariables(config, "", table, variables);
        }

        public static string ExtractLastVariable(string nestedName)
        {
            string[] nameParts = variableNameSplitRegex.Split(nestedName);
            if (nameParts.Length == 1)
            {
                //todo: error check
                return null;
            }

            return UnescapeKey(nameParts[nameParts.Length - 1]);
        }

        private static Type GenerateDictType(Type baseType)
        {
            if (!typeof (IDictionary).IsAssignableFrom(baseType))
            {
                //todo: handle error path
                return baseType;
            }
            Type[] arguments = baseType.GetGenericArguments();
            //if the value type is another dictionary, generate a type for this dictionary
            if (typeof (IDictionary).IsAssignableFrom(arguments[1]))
            {
                arguments[1] = GenerateDictType(arguments[1]);
            }
            return typeof(TableDictionary<,>).MakeGenericType(arguments);
        } 

        public static IDictionary<string, object> RetrieveState(TableDetails tableInfo, Collection<string> variables)
        {
            IDictionary<string, object> variableStates = new Dictionary<string, object>();
            if (tableInfo == null || variables == null || variables.Count == 0)
            {
                return variableStates;
            }

            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(tableInfo.Table);

            string labelPartitionKey = EscapeKey(tableInfo.PartitionKey) + "_labels";

            TableQuery<DynamicTableEntity> query = new TableQuery<DynamicTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, labelPartitionKey));

            var labels = table.ExecuteQuery(query);
            foreach (var label in labels)
            {
                string variableName = UnescapeKey(label.RowKey);
                if (variables.Contains(variableName))
                {
                    if (label.Properties.ContainsKey("Value"))
                    {
                        variableStates.Add(variableName, label.Properties["Value"].PropertyAsObject);
                    }
                    else
                    {
                        Type dictType = Type.GetType((string) label.Properties["type"].PropertyAsObject);
                        var finalType = GenerateDictType(dictType);
                        object[] args = new object[3];
                        args[0] = table;
                        args[1] = tableInfo.PartitionKey;
                        args[2] = variableName;
                        dynamic dict = Activator.CreateInstance(finalType, args);
                        variableStates.Add(variableName, dict);
                    }
                }
            }
            return variableStates;
        } 
    }
}
